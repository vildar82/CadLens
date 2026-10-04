using System.Collections.Immutable;
using System.Globalization;
using Xunit;

namespace CadLens.Lenses.Tests;

/// <summary>Numeric groups follow drawing precision while detached facts and filters stay exact.</summary>
public sealed class RoundedPropertyGroupingTests
{
    private const string Type = "AcDbPolyline";
    private static readonly LayerSnapshot Layer = new(new TestLayerId("layer"), "Roads", false, false, false, false);

    /// <summary>Measured near-zero areas share a zero group while missing areas remain unavailable.</summary>
    [Theory]
    [InlineData(DrawingGrouping.Layers)]
    [InlineData(DrawingGrouping.ObjectTypes)]
    public void AreaZeroGroupsMergeWithoutChangingObjectsOrFilters(DrawingGrouping grouping)
    {
        var precision = new DrawingPrecision();
        ImmutableArray<EntitySnapshot> entities =
        [
            Area("zero", 0), Area("positive", 0.0000001), Area("negative", -0.0000001),
            Area("larger", 0.0003), Area("missing", null)
        ];
        var type = Build(entities, grouping, DrawingPropertyId.Area, precision);
        var zero = type.Children.Single(group => group.Properties[0].Value is DrawingNumberValue {Value: 0});
        var unavailable = type.Children.Single(group => group.Properties[0].Value is null);

        Assert.Equal(3, type.Children.Length);
        Assert.Equal(["negative", "positive", "zero"], zero.Objects.Select(id => id.DisplayId));
        Assert.Equal("missing", Assert.Single(unavailable.Objects).DisplayId);
        Assert.Equal(0, BitConverter.DoubleToInt64Bits(Assert.IsType<DrawingNumberValue>(zero.Properties[0].Value).Value));
        Assert.Equal(5, type.Children.SelectMany(group => group.Objects).Distinct().Count());
        var objects = type.Children.SelectMany(group => group.Children).ToList();
        var ordered = objects.OrderBy(
            node => DrawingProperties.GetValue(node, DrawingPropertyId.Area),
            Comparer<DrawingValue?>.Create(DrawingProperties.CompareValues));
        Assert.Equal(["negative", "zero", "positive", "larger", "missing"], ordered.Select(node => node.Id));
        Assert.Equal(new DrawingNumberValue(0.0000001, DrawingUnit.Area),
            DrawingProperties.GetValue(objects.Single(node => node.Id == "positive"), DrawingPropertyId.Area));
        Assert.Equal(new DrawingNumberValue(0.0000001, DrawingUnit.Area), entities[1].Properties![DrawingPropertyId.Area]);

        var filter = new DrawingPropertyFilter(DrawingPropertyId.Area, DrawingFilterOperator.GreaterThan, new DrawingNumberValue(0, DrawingUnit.Area));
        var filtered = Build(entities, grouping, DrawingPropertyId.Area, precision, filter);
        Assert.Equal(["larger", "positive"], filtered.Objects.Select(id => id.DisplayId));
        Assert.Equal("positive", Assert.Single(filtered.Children.Single(group => group.Properties[0].Value is DrawingNumberValue {Value: 0}).Objects).DisplayId);
        Assert.Equal(5, Build(entities, grouping, DrawingPropertyId.Area, new DrawingPrecision(7)).Children.Length);
    }

    /// <summary>Angular grouping rounds in displayed degrees using angular precision instead of linear precision.</summary>
    [Theory]
    [InlineData(DrawingGrouping.Layers)]
    [InlineData(DrawingGrouping.ObjectTypes)]
    public void AnglesUseAngularPrecisionAndKeepRadiansInObjectFacts(DrawingGrouping grouping)
    {
        var key = DrawingPropertyKey.ForDynamicBlock("Rotation");
        ImmutableArray<EntitySnapshot> entities =
        [
            Dynamic("1", new DrawingNumberValue(10.041 * Math.PI / 180, DrawingUnit.Angle)),
            Dynamic("2", new DrawingNumberValue(10.049 * Math.PI / 180, DrawingUnit.Angle)),
            Dynamic("3", new DrawingNumberValue(10.06 * Math.PI / 180, DrawingUnit.Angle))
        ];
        var precision = new DrawingPrecision(8, 1);
        var type = Build(entities, grouping, key, precision);
        var combined = type.Children.Single(group => group.Count == 2);

        Assert.Equal(2, type.Children.Length);
        Assert.Equal(["1", "2"], combined.Objects.Select(id => id.DisplayId));
        var groupValue = Assert.IsType<DrawingNumberValue>(combined.Properties[0].Value);
        Assert.Equal(DrawingUnit.Angle, groupValue.Unit);
        Assert.Equal(10, precision.Round(groupValue.Value, groupValue.Unit));
        Assert.Equal(new DrawingNumberValue(10.041 * Math.PI / 180, DrawingUnit.Angle), DrawingProperties.GetValue(combined.Children[0], key));
        Assert.Equal(3, Build(entities, grouping, key, new DrawingPrecision(0, 3)).Children.Length);
    }

    /// <summary>Rounding cannot merge different numeric units, typed values, or numeric-looking attribute strings.</summary>
    [Theory]
    [InlineData(DrawingGrouping.Layers)]
    [InlineData(DrawingGrouping.ObjectTypes)]
    public void DrawingOwnedPropertiesRetainUnitsAndRawText(DrawingGrouping grouping)
    {
        var key = DrawingPropertyKey.ForDynamicBlock("Rotation");
        ImmutableArray<EntitySnapshot> entities =
        [
            Dynamic("distance", new DrawingNumberValue(0.0000001, DrawingUnit.Distance)) with
            {
                BlockAttributes = [new BlockAttributeSnapshot("VALUE", "0")]
            },
            Dynamic("area", new DrawingNumberValue(0.0000001, DrawingUnit.Area)) with
            {
                BlockAttributes = [new BlockAttributeSnapshot("VALUE", "0.0000001")]
            },
            Dynamic("scale", new DrawingNumberValue(0.0000001, DrawingUnit.Scale)),
            Dynamic("text", new DrawingTextValue("0")),
            Dynamic("boolean", new DrawingBooleanValue(false)),
            Dynamic("missing", null)
        ];
        var type = Build(entities, grouping, key, new DrawingPrecision());

        Assert.Equal(6, type.Children.Length);
        Assert.All(type.Children, group => Assert.Single(group.Objects));
        Assert.Equal(3, type.Children.Select(group => group.Properties[0].Value).OfType<DrawingNumberValue>().Select(value => value.Unit).Distinct().Count());
        var attributes = Build(entities, grouping, DrawingPropertyKey.ForAttribute("VALUE"), new DrawingPrecision());
        Assert.Equal(3, attributes.Children.Length);
        Assert.Contains(attributes.Children, group => group.Properties[0].Value == new DrawingTextValue("0.0000001"));
    }

    /// <summary>Unrepresentable angular display values join unavailable groups without changing original measurements.</summary>
    [Fact]
    public void AngularConversionOverflowGroupsAsUnavailable()
    {
        var key = DrawingPropertyKey.ForDynamicBlock("Rotation");
        ImmutableArray<EntitySnapshot> entities =
        [
            Dynamic("overflow", new DrawingNumberValue(double.MaxValue, DrawingUnit.Angle)),
            Dynamic("missing", null),
            Dynamic("nan", new DrawingNumberValue(double.NaN, DrawingUnit.Angle))
        ];
        var type = Build(entities, DrawingGrouping.ObjectTypes, key, new DrawingPrecision());

        Assert.Equal(3, Assert.Single(type.Children).Count);
        Assert.Null(type.Children[0].Properties[0].Value);
        Assert.Equal(new DrawingNumberValue(double.MaxValue, DrawingUnit.Angle),
            DrawingProperties.GetValue(type.Children[0].Children.Single(node => node.Id == "overflow"), key));
    }

    /// <summary>Display and grouping share clamped precision, midpoint direction, and count semantics.</summary>
    [Theory]
    [InlineData(2, DrawingUnit.Distance, 1.125, 1.13)]
    [InlineData(2, DrawingUnit.Area, -1.125, -1.13)]
    [InlineData(2, DrawingUnit.Scale, 1.125, 1.13)]
    [InlineData(8, DrawingUnit.Count, 1.5, 2)]
    [InlineData(-1, DrawingUnit.Distance, 1.5, 2)]
    [InlineData(20, DrawingUnit.Distance, 0.000000005, 0.00000001)]
    public void SharedRoundingPreservesDisplayRules(int digits, DrawingUnit unit, double value, double expected) =>
        Assert.Equal(expected, new DrawingPrecision(digits).Round(value, unit));

    /// <summary>Values collapsed by numeric formatting must also share a group at large magnitudes.</summary>
    [Theory]
    [InlineData(DrawingGrouping.Layers, 4, 100000000000.0001, 100000000000.0002)]
    [InlineData(DrawingGrouping.ObjectTypes, 4, 100000000000.0001, 100000000000.0002)]
    [InlineData(DrawingGrouping.Layers, 8, 12345678.12345611, 12345678.12345612)]
    [InlineData(DrawingGrouping.ObjectTypes, 8, 12345678.12345611, 12345678.12345612)]
    public void LargeAreasWithTheSameDisplayedValueShareAGroup(DrawingGrouping grouping, int digits, double first, double second)
    {
        var precision = new DrawingPrecision(digits);
        var format = precision.GetNumberFormat(DrawingUnit.Area);
        var caption = first.ToString(format, CultureInfo.InvariantCulture);
        var type = Build([Area("1", first), Area("2", second)], grouping, DrawingPropertyId.Area, precision);
        var group = Assert.Single(type.Children);
        var groupedValue = Assert.IsType<DrawingNumberValue>(group.Properties[0].Value);

        Assert.NotEqual(first, second);
        Assert.Equal(caption, second.ToString(format, CultureInfo.InvariantCulture));
        Assert.Equal(caption, groupedValue.Value.ToString(format, CultureInfo.InvariantCulture));
        Assert.Equal(2, group.Count);
        Assert.Equal(new DrawingNumberValue(first, DrawingUnit.Area), DrawingProperties.GetValue(group.Children[0], DrawingPropertyId.Area));
        Assert.Equal(new DrawingNumberValue(second, DrawingUnit.Area), DrawingProperties.GetValue(group.Children[1], DrawingPropertyId.Area));
    }

    /// <summary>Finite numeric limits never become unavailable when their rounded decimal caption overflows parsing.</summary>
    [Theory]
    [InlineData(double.MaxValue, DrawingUnit.Area)]
    [InlineData(double.MinValue, DrawingUnit.Area)]
    [InlineData(double.MaxValue, DrawingUnit.Count)]
    [InlineData(double.MinValue, DrawingUnit.Count)]
    public void FiniteLimitsRemainAvailable(double value, DrawingUnit unit)
    {
        var precision = new DrawingPrecision(8);
        var rounded = precision.Round(value, unit);
        var key = DrawingPropertyKey.ForDynamicBlock("Rotation");
        var type = Build([Dynamic("1", new DrawingNumberValue(value, unit))], DrawingGrouping.ObjectTypes, key, precision);

        Assert.Equal(value, rounded);
        Assert.Equal(new DrawingNumberValue(value, unit), Assert.Single(type.Children).Properties[0].Value);
        Assert.Equal(value.ToString(precision.GetNumberFormat(unit), CultureInfo.InvariantCulture),
            rounded.ToString(precision.GetNumberFormat(unit), CultureInfo.InvariantCulture));
    }

    /// <summary>Canonical angle groups retain their displayed degrees after storing the grouped value in radians.</summary>
    [Fact]
    public void RoundedAngleGroupRoundTripsThroughRadians()
    {
        var precision = new DrawingPrecision(0, 8);
        var key = DrawingPropertyKey.ForDynamicBlock("Rotation");
        const double first = 12345678.12345611 * (Math.PI / 180);
        const double second = 12345678.12345612 * (Math.PI / 180);
        var type = Build(
            [Dynamic("1", new DrawingNumberValue(first, DrawingUnit.Angle)), Dynamic("2", new DrawingNumberValue(second, DrawingUnit.Angle))],
            DrawingGrouping.ObjectTypes,
            key,
            precision);
        var group = Assert.Single(type.Children);
        var groupedValue = Assert.IsType<DrawingNumberValue>(group.Properties[0].Value);

        Assert.Equal(2, group.Count);
        Assert.Equal(precision.Round(first, DrawingUnit.Angle), precision.Round(groupedValue.Value, DrawingUnit.Angle));
        Assert.Equal(precision.Round(second, DrawingUnit.Angle), precision.Round(groupedValue.Value, DrawingUnit.Angle));
    }

    private static LensNode Build(
        ImmutableArray<EntitySnapshot> entities,
        DrawingGrouping grouping,
        DrawingPropertyKey key,
        DrawingPrecision precision,
        DrawingPropertyFilter? filter = null)
    {
        var presentation = DrawingLensProvider.Build(
            new DrawingInventory("Model", [Layer], entities, precision),
            grouping,
            [],
            new Dictionary<string, ImmutableArray<DrawingPropertyKey>> {[Type] = [key]},
            filter is null ? null : new Dictionary<string, DrawingPropertyFilter> {[Type] = filter});

        return grouping == DrawingGrouping.Layers ? presentation.Groups[0].Children[0] : presentation.Groups[0];
    }

    private static EntitySnapshot Area(string id, double? value) => new(
        new TestEntityId(id),
        Layer.Id,
        Type,
        ImmutableDictionary<DrawingPropertyId, DrawingValue?>.Empty.Add(
            DrawingPropertyId.Area,
            value is { } number ? new DrawingNumberValue(number, DrawingUnit.Area) : null));

    private static EntitySnapshot Dynamic(string id, DrawingValue? value) => new(
        new TestEntityId(id),
        Layer.Id,
        Type,
        DynamicBlockProperties: [new DynamicBlockPropertySnapshot("Rotation", value)]);
}
