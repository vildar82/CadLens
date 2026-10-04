using System.Collections.Immutable;
using Xunit;

namespace CadLens.Lenses.Tests;

/// <summary>Length totals follow detached group targets without borrowing primary row metrics.</summary>
public sealed class DrawingLengthTotalTests
{
    private const string TypeKey = "AcDbPolyline";

    /// <summary>Null, missing, nonfinite, and incompatible lengths stay out of the raw sum.</summary>
    [Theory]
    [InlineData(DrawingGrouping.Layers)]
    [InlineData(DrawingGrouping.ObjectTypes)]
    public void MixedLengthsKeepExactValuesAndUnavailableCounts(DrawingGrouping grouping)
    {
        var inventory = Inventory(
            Entity("1", new DrawingNumberValue(1.234, DrawingUnit.Distance)),
            Entity("2", new DrawingNumberValue(2.234, DrawingUnit.Distance)),
            Entity("3", null),
            Entity("4", new DrawingNumberValue(double.NaN, DrawingUnit.Distance)),
            Entity("5", new DrawingNumberValue(double.PositiveInfinity, DrawingUnit.Distance)),
            Entity("6", new DrawingNumberValue(double.NegativeInfinity, DrawingUnit.Distance)),
            Entity("7", new DrawingNumberValue(20, DrawingUnit.Count)),
            Entity("8", new DrawingTextValue("30")),
            Entity("9", null) with {Properties = null});
        var presentation = DrawingLensProvider.Build(inventory, grouping, []);
        var group = Assert.Single(presentation.Groups);

        Assert.Equal(9, group.Count);
        Assert.Equal(3.468, group.LengthTotal!.Value!.Value, 12);
        Assert.Equal(7, group.LengthTotal.UnavailableCount);
        var type = grouping == DrawingGrouping.Layers ? Assert.Single(group.Children) : group;
        Assert.Equal(DrawingPropertyId.Vertices, type.RowMetric!.Id);
        Assert.Equal(group.LengthTotal, type.LengthTotal);
        Assert.Equal(9, type.Objects.Length);
    }

    /// <summary>Grouping, visibility inclusion, and property filters use the same measured membership.</summary>
    [Theory]
    [InlineData(DrawingGrouping.Layers)]
    [InlineData(DrawingGrouping.ObjectTypes)]
    public void FilteredPropertyGroupsTotalOnlyIncludedTargets(DrawingGrouping grouping)
    {
        var inventory = Inventory(
            Entity("1", new DrawingNumberValue(2, DrawingUnit.Distance)),
            Entity("2", new DrawingNumberValue(3, DrawingUnit.Distance)),
            Entity("3", new DrawingNumberValue(30, DrawingUnit.Distance)),
            Entity("4", new DrawingNumberValue(4, DrawingUnit.Distance)) with {LayerId = new TestLayerId("off")});
        var fields = new Dictionary<string, ImmutableArray<DrawingPropertyId>>
        {
            [TypeKey] = [DrawingPropertyId.Closed]
        };
        var filters = new Dictionary<string, DrawingPropertyFilter>
        {
            [TypeKey] = new(DrawingPropertyId.Length, DrawingFilterOperator.LessThan, new DrawingNumberValue(10, DrawingUnit.Distance))
        };
        var group = Assert.Single(DrawingLensProvider.Build(inventory, grouping, [], fields, filters).Groups);
        var type = grouping == DrawingGrouping.Layers ? Assert.Single(group.Children) : group;
        var property = Assert.Single(type.Children);

        Assert.Equal(["1", "2"], group.Objects.Select(id => id.DisplayId));
        Assert.Equal(new DrawingLengthTotal(5, 0), group.LengthTotal);
        Assert.Equal(group.LengthTotal, type.LengthTotal);
        Assert.Equal(group.LengthTotal, property.LengthTotal);
        var included = DrawingLensProvider.Build(inventory, grouping, [DrawingLensProvider.IncludeOff], fields, filters);
        Assert.Equal(9, included.Groups.Sum(node => node.LengthTotal!.Value));
        Assert.Equal(3, included.Groups.Sum(node => node.Count));
    }

    /// <summary>Unsupported and unmeasured groups remain distinct from a measured zero.</summary>
    [Fact]
    public void NoUsableLengthsNeverImplyZero()
    {
        var unavailable = Group(Entity("1", null), Entity("2", null) with {Properties = null});
        Assert.Equal(new DrawingLengthTotal(null, 2), unavailable.LengthTotal);
        Assert.Null(Group(Entity("1", null) with {Properties = null}).LengthTotal);
        Assert.Equal(new DrawingLengthTotal(0, 0), Group(Entity("1", new DrawingNumberValue(0, DrawingUnit.Distance))).LengthTotal);
    }

    /// <summary>An overflowing subgroup cannot silently contribute zero to its layer total.</summary>
    [Fact]
    public void UnrepresentableSumDoesNotBecomeAPartialParentTotal()
    {
        var group = Group(
            Entity("1", new DrawingNumberValue(double.MaxValue, DrawingUnit.Distance)),
            Entity("2", new DrawingNumberValue(double.MaxValue, DrawingUnit.Distance)),
            Entity("3", new DrawingNumberValue(2, DrawingUnit.Distance)) with {TypeKey = "AcDbLine"});

        Assert.Equal(new DrawingLengthTotal(null, 0), group.LengthTotal);
    }

    private static LensNode Group(params EntitySnapshot[] entities) =>
        Assert.Single(DrawingLensProvider.Build(Inventory(entities), DrawingGrouping.Layers, []).Groups);

    private static DrawingInventory Inventory(params EntitySnapshot[] entities) => new(
        "Model",
        [
            new LayerSnapshot(new TestLayerId("on"), "On", false, false, false, false),
            new LayerSnapshot(new TestLayerId("off"), "Off", true, false, false, false)
        ],
        [.. entities]);

    private static EntitySnapshot Entity(string id, DrawingValue? length) => new(
        new TestEntityId(id),
        new TestLayerId("on"),
        TypeKey,
        new Dictionary<DrawingPropertyId, DrawingValue?>
        {
            [DrawingPropertyId.Length] = length,
            [DrawingPropertyId.Vertices] = new DrawingNumberValue(10, DrawingUnit.Count),
            [DrawingPropertyId.Closed] = new DrawingBooleanValue(false)
        }.ToImmutableDictionary(),
        DrawingPropertyId.Vertices);
}
