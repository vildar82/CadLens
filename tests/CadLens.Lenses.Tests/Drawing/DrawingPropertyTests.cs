using System.Collections.Immutable;
using System.Globalization;
using Xunit;

namespace CadLens.Lenses.Tests;

/// <summary>Behavioral checks for observed properties, composite groups, and displayed navigation.</summary>
public sealed class DrawingPropertyTests
{
    private static readonly LayerSnapshot Layer = new(new TestLayerId("layer"), "Roads", false, false, false, false);

    /// <summary>Measurements remain separate from placed targets and unsupported metrics remain unavailable.</summary>
    [Fact]
    public void MetricsRemainSeparateFromPlacedObjectCounts()
    {
        ImmutableArray<EntitySnapshot> entities =
        [
            Metric("polyline", "AcDbPolyline", DrawingPropertyId.Vertices, 120),
            Metric("block", "AcDbBlockReference", DrawingPropertyId.DefinitionEntities, 300),
            Metric("missing-block", "AcDbBlockReference", DrawingPropertyId.DefinitionEntities, null),
            Entity("custom", "Custom.Object")
        ];
        var presentation = Build(entities);
        var objects = presentation.Groups.SelectMany(node => node.Children).ToDictionary(node => node.Id);

        Assert.All(objects.Values, node => Assert.Equal(1, node.Count));
        Assert.Equal(120, objects["polyline"].RowMetric!.Value!.Value);
        Assert.Equal(300, objects["block"].RowMetric!.Value!.Value);
        Assert.Equal(DrawingPropertyId.DefinitionEntities, objects["missing-block"].RowMetric!.Id);
        Assert.Null(objects["missing-block"].RowMetric!.Value);
        Assert.Null(objects["custom"].RowMetric);
        Assert.Equal(4, presentation.Groups.Sum(node => node.Count));
        Assert.Equal(4, presentation.Groups.SelectMany(node => node.Objects).Distinct().Count());
        Assert.Same(entities[0], presentation.Inventory!.Entities[0]);
    }

    /// <summary>Unknown runtime types gain details, metrics, and grouping choices from their observed dictionary.</summary>
    [Fact]
    public void ObservedPropertiesDoNotRequireAPrimitiveTypeCatalog()
    {
        var first = Metric(
            "1",
            "Custom.Curve",
            DrawingPropertyId.Length,
            15,
            Properties((DrawingPropertyId.PatternAngle, new DrawingNumberValue(0.25, DrawingUnit.Angle))));
        var second = Entity("2", "Custom.Curve", Properties((DrawingPropertyId.Width, null)));
        var presentation = Build([first, second], fields: [DrawingPropertyId.PatternAngle]);

        Assert.Equal<DrawingPropertyKey>(
            [
                DrawingPropertyId.Layer, DrawingPropertyId.Length, DrawingPropertyId.Width,
                DrawingPropertyId.PatternAngle
            ],
            DrawingProperties.GetAvailableFields([first, second]));
        Assert.Equal(2, presentation.Groups[0].Children.Length);
        Assert.Equal(DrawingPropertyId.Length, presentation.Groups[0].RowMetric!.Id);
        Assert.Contains(DrawingProperties.GetDetails(first, Layer), field => field.Label == "Pattern angle");
        Assert.Equal(15, DrawingProperties.GetPrimaryMetric(first)!.Value!.Value);
    }

    /// <summary>Drawing-owned block names supply readable rows without influencing target counts.</summary>
    [Fact]
    public void BlockRowsUseObservedNameAndMetric()
    {
        var entity = Metric(
            "A1",
            "AcDbMInsertBlock",
            DrawingPropertyId.DefinitionEntities,
            30,
            Properties((DrawingPropertyId.BlockName, new DrawingTextValue("Yes"))));
        var node = Build([entity]).Groups[0].Children[0];

        Assert.Equal("Yes A1", node.Label);
        Assert.Equal(1, node.Count);
        Assert.Equal(30, node.RowMetric!.Value!.Value);
        Assert.Contains(DrawingPropertyId.BlockName, DrawingProperties.GetAvailableFields([entity]));
        Assert.Equal(
            new DrawingTextValue("Yes"),
            DrawingProperties.GetValue(entity, Layer, DrawingPropertyId.BlockName));
    }

    /// <summary>Both lenses create one composite level with stable keys and exactly partitioned targets.</summary>
    [Theory]
    [InlineData(DrawingGrouping.Layers)]
    [InlineData(DrawingGrouping.ObjectTypes)]
    public void CombinedGroupingUsesTypedEqualityAndCanonicalFieldOrder(DrawingGrouping grouping)
    {
        var properties = Appearance(new AssignedColor(AssignedColorKind.Index, 1));
        ImmutableArray<EntitySnapshot> entities =
        [
            Metric("3", "AcDbPolyline", DrawingPropertyId.Vertices, 9, properties),
            Metric("1", "AcDbPolyline", DrawingPropertyId.Vertices, 2, properties),
            Metric(
                "2",
                "AcDbPolyline",
                DrawingPropertyId.Vertices,
                3,
                properties.SetItem(DrawingPropertyId.Linetype, new DrawingTextValue("DASHED")))
        ];
        var first = Build(
            entities,
            grouping,
            [DrawingPropertyId.Linetype, DrawingPropertyId.Color, DrawingPropertyId.Color]);
        var reordered = Build([.. entities.Reverse()], grouping, [DrawingPropertyId.Color, DrawingPropertyId.Linetype]);
        var firstType = grouping == DrawingGrouping.Layers ? first.Groups[0].Children[0] : first.Groups[0];
        var secondType = grouping == DrawingGrouping.Layers ? reordered.Groups[0].Children[0] : reordered.Groups[0];

        Assert.Equal(3, firstType.Count);
        Assert.Equal(2, firstType.Children.Length);
        Assert.All(
            firstType.Children,
            node =>
            {
                Assert.Equal(LensNodeKind.PropertyGroup, node.Kind);
                Assert.Equal(
                    [DrawingPropertyId.Color, DrawingPropertyId.Linetype],
                    node.Properties.Select(property => property.Id));
                Assert.Equal(node.Count, node.Children.Length);
                Assert.All(node.Children, child => Assert.Equal(LensNodeKind.Object, child.Kind));
            });
        Assert.Equal(firstType.Children.Select(node => node.Id), secondType.Children.Select(node => node.Id));
        Assert.Equal(3, firstType.Children.SelectMany(node => node.Objects).Distinct().Count());
        var continuous = firstType.Children.Single(node => node.Count == 2);
        Assert.Equal(["1", "3"], continuous.Objects.Select(id => id.DisplayId));
        Assert.Equal(continuous.Objects, continuous.Children.SelectMany(node => node.Objects));
    }

    /// <summary>Assigned inheritance modes stay distinct from explicit appearance.</summary>
    [Fact]
    public void ByLayerAndExplicitAppearanceDoNotMerge()
    {
        var first = Entity("1", "AcDbLine", Appearance(new AssignedColor(AssignedColorKind.ByLayer)));
        var second = Entity("2", "AcDbLine", Appearance(new AssignedColor(AssignedColorKind.Index, 1)));
        var presentation = Build([first, second], fields: [DrawingPropertyId.Color]);

        Assert.Equal(2, presentation.Groups[0].Children.Length);
        Assert.All(presentation.Groups[0].Children, node => Assert.Single(node.Objects));
    }

    /// <summary>Renaming a layer changes the caption while retaining the property group identity and selection.</summary>
    [Fact]
    public void LayerGroupingIdentitySurvivesDrawingNameChanges()
    {
        var entity = Entity("1", "AcDbLine");
        var original = Build([entity], fields: [DrawingPropertyId.Layer]);
        var renamedLayer = Layer with {Name = "Renamed roads"};
        var renamed = DrawingLensProvider.Build(
            new DrawingInventory("Model", [renamedLayer], [entity]),
            DrawingGrouping.ObjectTypes,
            new HashSet<string>(),
            new Dictionary<string, ImmutableArray<DrawingPropertyKey>> {["AcDbLine"] = [DrawingPropertyId.Layer]});
        var originalGroup = original.Groups[0].Children[0];
        var navigation = new NavigationState();
        navigation.Reset(original.Groups, false);
        navigation.Enter("AcDbLine");
        navigation.Enter(originalGroup.Id);
        navigation.Reset(renamed.Groups, true);

        Assert.Equal(originalGroup.Id, renamed.Groups[0].Children[0].Id);
        Assert.Equal(originalGroup.Id, navigation.Current!.Id);
        Assert.Equal("Renamed roads", Assert.IsType<DrawingLayerValue>(navigation.Current.Properties[0].Value).Name);
        Assert.Equal(new DrawingLayerValue(Layer.Id, Layer.Name), new DrawingLayerValue(Layer.Id, renamedLayer.Name));
    }

    /// <summary>A present unavailable value remains distinct from a measured zero.</summary>
    [Fact]
    public void MissingWidthDoesNotGroupWithZeroWidth()
    {
        var first = Entity("1", "AcDbPolyline", Properties((DrawingPropertyId.Width, null)));
        var second = Entity(
            "2",
            "AcDbPolyline",
            Properties((DrawingPropertyId.Width, new DrawingNumberValue(0, DrawingUnit.Distance))));
        var groups = Build([first, second], fields: [DrawingPropertyId.Width]).Groups[0].Children;

        Assert.Equal(2, groups.Length);
        Assert.Contains(groups, node => node.Properties[0].Value is null);
        Assert.Contains(groups, node => node.Properties[0].Value is DrawingNumberValue {Value: 0});
        Assert.Equal(2, groups.Select(node => node.Id).Distinct().Count());
    }

    /// <summary>Color book identity cannot collide through display concatenation.</summary>
    [Fact]
    public void ColorBookIdentityKeepsBookAndEntrySeparate()
    {
        var first = Entity(
            "1",
            "AcDbLine",
            Appearance(new AssignedColor(AssignedColorKind.ColorBook, Name: "B$C", BookName: "A")));
        var second = Entity(
            "2",
            "AcDbLine",
            Appearance(new AssignedColor(AssignedColorKind.ColorBook, Name: "C", BookName: "A$B")));
        var groups = Build([first, second], fields: [DrawingPropertyId.Color]).Groups[0].Children;

        Assert.Equal(2, groups.Length);
        Assert.Equal(2, groups.Select(node => node.Id).Distinct().Count());
    }

    /// <summary>Numeric group identities retain their hexadecimal form across display cultures and frameworks.</summary>
    [Fact]
    public void NumericGroupingIdentityDoesNotDependOnCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var entity = Entity(
            "1",
            "AcDbHatch",
            Properties(
                (DrawingPropertyId.PatternAngle, new DrawingNumberValue(0.125, DrawingUnit.Angle)),
                (DrawingPropertyId.PatternScale, new DrawingNumberValue(1.25, DrawingUnit.Scale))));

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var english = Build([entity], fields: [DrawingPropertyId.PatternAngle, DrawingPropertyId.PatternScale]);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            var russian = Build([entity], fields: [DrawingPropertyId.PatternScale, DrawingPropertyId.PatternAngle]);

            Assert.Equal(english.Groups[0].Children[0].Id, russian.Groups[0].Children[0].Id);
            Assert.Equal(
                "properties:D227E1465E5B6C12BE74FE81BF486FA9B81DA5BB35D005A292325F58B2CF8EA6",
                english.Groups[0].Children[0].Id);
            Assert.Equal(
                0.125,
                Assert.IsType<DrawingNumberValue>(english.Groups[0].Children[0].Properties[0].Value).Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    /// <summary>Nonfinite numbers become unavailable and signed zero cannot split a numeric group.</summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NumericValuesAreNormalizedConsistently(double invalidValue)
    {
        var invalid = Metric("1", "Custom.Curve", DrawingPropertyId.Length, invalidValue);
        var positiveZero = Metric("2", "Custom.Curve", DrawingPropertyId.Length, 0);
        var negativeZero = Metric("3", "Custom.Curve", DrawingPropertyId.Length, -0d);
        var groups = Build([invalid, positiveZero, negativeZero], fields: [DrawingPropertyId.Length]).Groups[0]
            .Children;

        Assert.Null(DrawingProperties.GetPrimaryMetric(invalid)!.Value);
        Assert.Null(DrawingProperties.GetValue(invalid, Layer, DrawingPropertyId.Length));
        Assert.Contains(
            DrawingProperties.GetDetails(invalid, Layer),
            field => field is {Label: "Length", TypedValue: null});
        Assert.Equal(
            0L,
            BitConverter.DoubleToInt64Bits(
                Assert.IsType<DrawingNumberValue>(
                    DrawingProperties.GetValue(negativeZero, Layer, DrawingPropertyId.Length)).Value));
        Assert.Equal(2, groups.Length);
        Assert.Contains(groups, node => node.Count == 2);
    }

    /// <summary>Details and grouping options follow present keys, distinguishing unsupported and unavailable facts.</summary>
    [Fact]
    public void DetailsAndChoicesUseOnlyObservedProperties()
    {
        var entity = Entity(
            "1",
            "AcDbCircle",
            Properties(
                (DrawingPropertyId.StartAngle, null),
                (DrawingPropertyId.Radius, new DrawingNumberValue(5, DrawingUnit.Distance)),
                (DrawingPropertyId.Layer, new DrawingLayerValue(Layer.Id, Layer.Name))));
        var fields = DrawingProperties.GetDetails(entity, Layer);

        Assert.Equal(["Radius", "Start angle"], fields.Select(field => field.Label));
        Assert.Contains(fields, field => field is {Label: "Start angle", TypedValue: null});
        Assert.DoesNotContain(fields, field => field.Label is "Layer" or "End angle");
        Assert.Equal<DrawingPropertyKey>(
            [DrawingPropertyId.Layer, DrawingPropertyId.Radius, DrawingPropertyId.StartAngle],
            DrawingProperties.GetAvailableFields([entity]));
        Assert.Equal<DrawingPropertyKey>(
            [DrawingPropertyId.Layer],
            DrawingProperties.GetAvailableFields([Entity("2", "AcDbLine")]));
        Assert.Empty(DrawingProperties.GetDetails(Entity("2", "AcDbLine"), Layer));
    }

    /// <summary>Navigation getters and object moves reuse the current sorting projection.</summary>
    [Fact]
    public void NavigationReusesDisplayedProjectionUntilSortChanges()
    {
        var presentation = Build(
        [
            Metric("1", "AcDbLine", DrawingPropertyId.Length, 1), Metric("2", "AcDbLine", DrawingPropertyId.Length, 2)
        ]);
        var navigation = new NavigationState();
        var projectionCalls = 0;
        navigation.SetItemOrder(items =>
        {
            projectionCalls++;
            return items.OrderBy(node => node.RowMetric?.Value?.Value);
        });
        navigation.Reset(presentation.Groups, false);
        navigation.Enter("AcDbLine");
        navigation.Enter("1");
        var callsAtSelection = projectionCalls;

        Assert.Equal(1, navigation.Position);
        Assert.Equal(2, navigation.ObjectCount);
        Assert.False(navigation.CanPrevious);
        Assert.True(navigation.CanNext);
        Assert.Empty(navigation.Items);
        navigation.MoveObject(1);
        Assert.Equal(2, navigation.Position);
        Assert.Equal(callsAtSelection, projectionCalls);
    }

    /// <summary>The selected object finds its new ancestors after regrouping and membership changes.</summary>
    [Fact]
    public void NavigationRetainsSelectedObjectAcrossGroupingChanges()
    {
        var properties = Appearance(new AssignedColor(AssignedColorKind.Index, 1));
        var first = Metric("1", "AcDbLine", DrawingPropertyId.Length, 12, properties);
        var second = Metric("2", "AcDbLine", DrawingPropertyId.Length, 24, properties);
        var original = Build([first, second]);
        var navigation = new NavigationState();
        navigation.Reset(original.Groups, false);
        Assert.True(navigation.Enter("AcDbLine"));
        Assert.True(navigation.Enter("1"));
        navigation.Reset(Build([first, second], fields: [DrawingPropertyId.Color]).Groups, true);

        Assert.Equal("1", navigation.Current!.Id);
        Assert.Equal(3, navigation.Path.Count);
        Assert.Equal(1, navigation.Position);
        Assert.True(navigation.CanNext);
        var moved = first with
        {
            Properties = first.Properties!.SetItem(
                DrawingPropertyId.Color,
                new DrawingColorValue(new AssignedColor(AssignedColorKind.Index, 2)))
        };
        navigation.Reset(Build([moved, second], fields: [DrawingPropertyId.Color]).Groups, true);
        Assert.Equal("1", navigation.Current!.Id);
        Assert.Equal(1, navigation.ObjectCount);
        Assert.False(navigation.CanNext);
        navigation.Reset(original.Groups, true);
        Assert.Equal(["AcDbLine", "1"], navigation.Path.Select(node => node.Id));
    }

    /// <summary>Previous and Next follow displayed numeric ordering, including a sort change at object details.</summary>
    [Fact]
    public void NavigationUsesDisplayedNumericOrdering()
    {
        var presentation = Build(
        [
            Metric("1", "AcDbPolyline", DrawingPropertyId.Vertices, 100),
            Metric("2", "AcDbPolyline", DrawingPropertyId.Vertices, 2),
            Metric("3", "AcDbPolyline", DrawingPropertyId.Vertices, 10),
            Metric("4", "AcDbPolyline", DrawingPropertyId.Vertices, null)
        ]);
        var navigation = new NavigationState();
        navigation.SetItemOrder(items => items.OrderBy(node => node.RowMetric?.Value is null)
            .ThenBy(node => node.RowMetric?.Value?.Value));
        navigation.Reset(presentation.Groups, false);
        navigation.Enter("AcDbPolyline");

        Assert.Equal(["2", "3", "1", "4"], navigation.Items.Select(node => node.Id));
        navigation.Enter("2");
        navigation.MoveObject(1);
        Assert.Equal("3", navigation.Current!.Id);
        Assert.Equal(2, navigation.Position);
        navigation.SetItemOrder(items => items.OrderBy(node => node.RowMetric?.Value is null)
            .ThenByDescending(node => node.RowMetric?.Value?.Value));
        Assert.Equal(2, navigation.Position);
        navigation.MoveObject(1);
        Assert.Equal("2", navigation.Current!.Id);
        navigation.MoveObject(1);
        Assert.Equal("4", navigation.Current!.Id);
        Assert.False(navigation.CanNext);
    }

    private static LensPresentation Build(
        ImmutableArray<EntitySnapshot> entities,
        DrawingGrouping grouping = DrawingGrouping.ObjectTypes,
        ImmutableArray<DrawingPropertyKey> fields = default)
    {
        var selected = fields.IsDefaultOrEmpty
            ? null
            : entities.Select(entity => entity.TypeKey).Distinct().ToDictionary(type => type, _ => fields);

        return DrawingLensProvider.Build(
            new DrawingInventory("Model", [Layer], entities),
            grouping,
            new HashSet<string>(),
            selected);
    }

    private static EntitySnapshot Entity(
        string id,
        string typeKey,
        ImmutableDictionary<DrawingPropertyId, DrawingValue?>? properties = null) =>
        new(new TestEntityId(id), Layer.Id, typeKey, properties);

    private static EntitySnapshot Metric(
        string id,
        string typeKey,
        DrawingPropertyId metric,
        double? value,
        ImmutableDictionary<DrawingPropertyId, DrawingValue?>? properties = null) =>
        new(
            new TestEntityId(id),
            Layer.Id,
            typeKey,
            (properties ?? ImmutableDictionary<DrawingPropertyId, DrawingValue?>.Empty).SetItem(
                metric,
                value is null ? null : new DrawingNumberValue(value.Value, DrawingUnit.Count)),
            metric);

    private static ImmutableDictionary<DrawingPropertyId, DrawingValue?> Properties(
        params (DrawingPropertyId Id, DrawingValue? Value)[] values) =>
        values.ToImmutableDictionary(pair => pair.Id, pair => pair.Value);

    private static ImmutableDictionary<DrawingPropertyId, DrawingValue?> Appearance(AssignedColor color) => Properties(
        (DrawingPropertyId.Color, new DrawingColorValue(color)),
        (DrawingPropertyId.Linetype, new DrawingTextValue("Continuous")),
        (DrawingPropertyId.Lineweight,
            new DrawingLineweightValue(new AssignedLineweight(AssignedLineweightKind.Explicit, 25))),
        (DrawingPropertyId.LinetypeScale, new DrawingNumberValue(1, DrawingUnit.Scale)),
        (DrawingPropertyId.Transparency,
            new DrawingTransparencyValue(new AssignedTransparency(AssignedTransparencyKind.Explicit))));
}