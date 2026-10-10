using System.Collections.Immutable;
using System.Globalization;
using Xunit;

namespace CadLens.Lenses.Tests;

/// <summary>Drawing-owned properties share typed exploration without name or value collisions.</summary>
public sealed class BlockPropertyTests
{
    private const string BlockType = "AcDbBlockReference";
    private static readonly LayerSnapshot Layer = new(new TestLayerId("layer"), "Roads", false, false, false, false);

    /// <summary>Persistence retains the source and every character of a drawing-owned name.</summary>
    [Theory]
    [InlineData("Layer")]
    [InlineData("Area")]
    [InlineData("attribute:Layer")]
    [InlineData("attribute: MARK;dynamic:Width: ")]
    [InlineData("dynamic:Ширина;:=\n")]
    public void KeysRoundTripWithoutNormalizingNames(string identity)
    {
        Assert.True(DrawingPropertyKey.TryParse(identity, out var key));
        Assert.Equal(identity, key.ToString());
        Assert.True(DrawingPropertyKey.TryParse(key.ToString(), out var restored));
        Assert.Equal(key, restored);
    }

    /// <summary>Unknown enum names and incomplete drawing-owned names do not become valid keys.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("attribute:")]
    [InlineData("dynamic:")]
    [InlineData("layer")]
    [InlineData("0")]
    [InlineData(" Layer")]
    public void MalformedKeysAreRejected(string? identity) => Assert.False(DrawingPropertyKey.TryParse(identity, out _));

    /// <summary>Identical captions from different origins and differently cased tags have distinct identities.</summary>
    [Fact]
    public void DiscoveryKeepsSourcesAndExactNamesSeparate()
    {
        var entity = Block("1",
            (DrawingPropertyKey.ForAttribute("Layer"), new DrawingTextValue("Attribute layer")),
            (DrawingPropertyKey.ForAttribute("layer"), new DrawingTextValue("Lowercase")),
            (DrawingPropertyKey.ForAttribute(" "), new DrawingTextValue("Whitespace tag")),
            (DrawingPropertyKey.ForDynamicBlock("Layer"), new DrawingTextValue("Dynamic layer")));
        var available = DrawingProperties.GetAvailableFields([entity]);
        DrawingPropertyKey builtIn = DrawingPropertyId.Layer;
        var attribute = DrawingPropertyKey.ForAttribute("Layer");
        var dynamic = DrawingPropertyKey.ForDynamicBlock("Layer");

        Assert.Equal(6, available.Length);
        Assert.Contains(builtIn, available);
        Assert.Contains(attribute, available);
        Assert.Contains(dynamic, available);
        Assert.Contains(DrawingPropertyKey.ForAttribute("layer"), available);
        Assert.Contains(DrawingPropertyKey.ForAttribute(" "), available);
        Assert.NotEqual(builtIn, attribute);
        Assert.NotEqual(attribute, dynamic);
        Assert.Equal(new DrawingLayerValue(Layer.Id, Layer.Name), DrawingProperties.GetValue(entity, Layer, builtIn));
        Assert.Equal(new DrawingTextValue("Attribute layer"), DrawingProperties.GetValue(entity, Layer, attribute));
        Assert.Equal(new DrawingTextValue("Dynamic layer"), DrawingProperties.GetValue(entity, Layer, dynamic));
        Assert.Equal("Layer", DrawingProperties.GetLabel(attribute));
    }

    /// <summary>Unavailable, missing, and blank attribute scalars remain distinct in the unified dictionary.</summary>
    [Theory]
    [InlineData(DrawingGrouping.Layers)]
    [InlineData(DrawingGrouping.ObjectTypes)]
    public void DuplicateMissingAndBlankAttributesRemainDistinct(DrawingGrouping grouping)
    {
        var key = DrawingPropertyKey.ForAttribute("MARK");
        ImmutableArray<EntitySnapshot> entities =
        [
            Block("duplicate", (key, null)),
            Block("missing"),
            Block("unreadable", (key, null)),
            Block("blank", (key, new DrawingTextValue(""))),
            Block("uppercase", (key, new DrawingTextValue("Yes"))),
            Block("lowercase", (key, new DrawingTextValue("yes")))
        ];
        var type = GetType(Build(entities, grouping, [key]), grouping);
        var unavailable = type.Children.Single(node => node.Properties[0].Value is null);
        var duplicate = unavailable.Children.Single(node => node.Id == "duplicate");
        var details = duplicate.Fields.Where(field => field.PropertyKey == key).ToList();

        Assert.Equal(4, type.Children.Length);
        Assert.Equal(["duplicate", "missing", "unreadable"], unavailable.Objects.Select(id => id.DisplayId));
        Assert.Equal("Unavailable", Assert.Single(details).Value);
        Assert.Equal(new DrawingTextValue(""), DrawingProperties.GetValue(entities[3], Layer, key));
        Assert.Equal(6, type.Children.SelectMany(node => node.Objects).Distinct().Count());
        Assert.All(type.Children, node => Assert.Equal(key, node.Fields[0].PropertyKey));
    }

    /// <summary>Typed dynamic values use the common numeric ordering and exact-unit filters in both lenses.</summary>
    [Theory]
    [InlineData(DrawingGrouping.Layers)]
    [InlineData(DrawingGrouping.ObjectTypes)]
    public void DynamicNumbersUseSharedGroupingFilteringAndSorting(DrawingGrouping grouping)
    {
        var key = DrawingPropertyKey.ForDynamicBlock("Length");
        ImmutableArray<EntitySnapshot> entities =
        [
            Dynamic("ten", "Length", new DrawingNumberValue(10, DrawingUnit.Distance)),
            Dynamic("two", "Length", new DrawingNumberValue(2, DrawingUnit.Distance)),
            Dynamic("missing", "Other", new DrawingNumberValue(3, DrawingUnit.Distance)),
            Dynamic("incompatible", "Length", new DrawingNumberValue(2, DrawingUnit.Angle))
        ];
        var filter = new DrawingPropertyFilter(key, DrawingFilterOperator.LessThan, new DrawingNumberValue(5, DrawingUnit.Distance));
        var filtered = GetType(Build(entities, grouping, [key], filter), grouping);

        Assert.Equal("two", Assert.Single(filtered.Objects).DisplayId);
        Assert.Equal("two", Assert.Single(filtered.Children).Objects[0].DisplayId);
        var unfiltered = GetType(Build(entities, grouping, []), grouping);
        var ordered = unfiltered.Children.OrderBy(
            node => DrawingProperties.GetValue(node, key),
            Comparer<DrawingValue?>.Create(DrawingProperties.CompareValues));

        Assert.Equal(["two", "ten", "incompatible", "missing"], ordered.Select(node => node.Id));
        Assert.Equal(new DrawingNumberValue(10, DrawingUnit.Distance),
            unfiltered.Children.Single(node => node.Id == "ten").Fields.Single(field => field.PropertyKey == key).TypedValue);
    }

    /// <summary>Duplicate names, unreadable values, and nonfinite numbers cannot become scalar values.</summary>
    [Fact]
    public void DynamicUnavailableValuesNeverMatchInequality()
    {
        var key = DrawingPropertyKey.ForDynamicBlock("Width");
        ImmutableArray<EntitySnapshot> entities =
        [
            Dynamic("duplicate", "Width", null),
            Dynamic("null", "Width", null),
            Dynamic("nan", "Width", new DrawingNumberValue(double.NaN, DrawingUnit.Distance)),
            Block("missing")
        ];
        var filter = new DrawingPropertyFilter(key, DrawingFilterOperator.NotEqual, new DrawingNumberValue(1, DrawingUnit.Distance));

        Assert.All(entities, entity => Assert.Null(DrawingProperties.GetValue(entity, Layer, key)));
        Assert.All(entities, entity => Assert.False(filter.Matches(entity, Layer)));
        Assert.All(entities.Take(3), entity => Assert.Null(Assert.Single(
            DrawingProperties.GetDetails(entity, Layer).Where(field => field.PropertyKey == key)).TypedValue));
        Assert.Equal(4, Assert.Single(GetType(Build(entities, DrawingGrouping.ObjectTypes, [key]), DrawingGrouping.ObjectTypes).Children).Count);
    }

    /// <summary>Attribute text is raw and dynamic text, angle, and boolean conditions use their original types.</summary>
    [Fact]
    public void TypedFiltersDoNotParseOrTranslateDrawingValues()
    {
        var attribute = DrawingPropertyKey.ForAttribute("MARK");
        var angle = DrawingPropertyKey.ForDynamicBlock("Rotation");
        var visible = DrawingPropertyKey.ForDynamicBlock("Visible");
        var entity = Block("1",
            (attribute, new DrawingTextValue(" 001\nYes ")),
            (angle, new DrawingNumberValue(0.123456789, DrawingUnit.Angle)),
            (visible, new DrawingBooleanValue(false)));

        Assert.Equal(new DrawingTextValue(" 001\nYes "), DrawingProperties.GetValue(entity, Layer, attribute));
        Assert.True(new DrawingPropertyFilter(attribute, DrawingFilterOperator.Contains, new DrawingTextValue("001\nyes")).Matches(entity, Layer));
        Assert.False(new DrawingPropertyFilter(attribute, DrawingFilterOperator.Equal, new DrawingNumberValue(1, DrawingUnit.Count)).Matches(entity, Layer));
        Assert.True(new DrawingPropertyFilter(angle, DrawingFilterOperator.Equal, new DrawingNumberValue(0.123456789, DrawingUnit.Angle)).Matches(entity, Layer));
        Assert.True(new DrawingPropertyFilter(visible, DrawingFilterOperator.Equal, new DrawingBooleanValue(false)).Matches(entity, Layer));
    }

    /// <summary>Composite identities escape source names and remain stable across cultures and selection order.</summary>
    [Fact]
    public void CompositeIdentitiesEscapeNamesAndUseCanonicalOrder()
    {
        var attribute = DrawingPropertyKey.ForAttribute("MARK;text:False:WA==;dynamic:Width");
        var dynamic = DrawingPropertyKey.ForDynamicBlock(attribute.Name);
        var entity = Block("1",
            (attribute, new DrawingTextValue("X")),
            (DrawingPropertyKey.ForAttribute("MARK"), new DrawingTextValue("X")),
            (dynamic, new DrawingTextValue("X")));
        var culture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var first = Build([entity], DrawingGrouping.ObjectTypes, [attribute, dynamic, DrawingPropertyId.Layer]);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            var reordered = Build([entity], DrawingGrouping.ObjectTypes, [DrawingPropertyId.Layer, dynamic, attribute, attribute]);
            Assert.Equal(first.Groups[0].Children[0].Id, reordered.Groups[0].Children[0].Id);
            var ids = new[] {attribute, dynamic, DrawingPropertyKey.ForAttribute("MARK")}
                .Select(key => Build([entity], DrawingGrouping.ObjectTypes, [key]).Groups[0].Children[0].Id);
            Assert.Equal(3, ids.Distinct().Count());
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    private static LensNode GetType(LensPresentation presentation, DrawingGrouping grouping) =>
        grouping == DrawingGrouping.Layers ? presentation.Groups[0].Children[0] : presentation.Groups[0];

    private static LensPresentation Build(
        ImmutableArray<EntitySnapshot> entities,
        DrawingGrouping grouping,
        ImmutableArray<DrawingPropertyKey> fields,
        DrawingPropertyFilter? filter = null) => DrawingLensProvider.Build(
        new DrawingInventory("Model", [Layer], entities),
        grouping,
        [],
        new Dictionary<string, ImmutableArray<DrawingPropertyKey>> {[BlockType] = fields},
        filter is null ? null : new Dictionary<string, DrawingPropertyFilter> {[BlockType] = filter});

    private static EntitySnapshot Dynamic(string id, string name, DrawingValue? value) =>
        Block(id, (DrawingPropertyKey.ForDynamicBlock(name), value));

    private static EntitySnapshot Block(string id, params (DrawingPropertyKey Key, DrawingValue? Value)[] properties) => new(
        new TestEntityId(id),
        Layer.Id,
        BlockType,
        properties.ToImmutableDictionary(pair => pair.Key, pair => pair.Value)
            .Add(DrawingPropertyId.Attributes, new DrawingNumberValue(0, DrawingUnit.Count)));
}
