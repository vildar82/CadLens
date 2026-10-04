using System.Collections.Immutable;
using Xunit;

namespace CadLens.Lenses.Tests;

/// <summary>Property conditions keep exact typed values and limit every displayed action target.</summary>
public sealed class DrawingPropertyFilterTests
{
    private const string PolylineType = "AcDbPolyline";

    /// <summary>Area comparisons use square drawing units and do not treat missing geometry as zero.</summary>
    [Fact]
    public void AreaFilteringKeepsExactValuesAndUnits()
    {
        var filter = new DrawingPropertyFilter(
            DrawingPropertyId.Area,
            DrawingFilterOperator.GreaterThan,
            new DrawingNumberValue(12.3451, DrawingUnit.Area));

        Assert.True(filter.Matches(new DrawingNumberValue(12.3452, DrawingUnit.Area)));
        Assert.False(filter.Matches(new DrawingNumberValue(12.3451, DrawingUnit.Area)));
        Assert.False(filter.Matches(new DrawingNumberValue(12.3452, DrawingUnit.Distance)));
        Assert.False(filter.Matches(null));

        var zero = filter with {Operator = DrawingFilterOperator.Equal, Value = new DrawingNumberValue(0, DrawingUnit.Area)};
        Assert.True(zero.Matches(new DrawingNumberValue(0, DrawingUnit.Area)));
        Assert.False(zero.Matches(null));
    }

    /// <summary>Comparisons use the original value, including differences below display precision.</summary>
    [Theory]
    [InlineData(DrawingFilterOperator.Equal, 12.3451, true)]
    [InlineData(DrawingFilterOperator.Equal, 12.3452, false)]
    [InlineData(DrawingFilterOperator.NotEqual, 12.3451, false)]
    [InlineData(DrawingFilterOperator.NotEqual, 12.3452, true)]
    [InlineData(DrawingFilterOperator.LessThan, 12.3451, false)]
    [InlineData(DrawingFilterOperator.LessThan, 12.3452, true)]
    [InlineData(DrawingFilterOperator.LessThanOrEqual, 12.3451, true)]
    [InlineData(DrawingFilterOperator.LessThanOrEqual, 12.3450, false)]
    [InlineData(DrawingFilterOperator.GreaterThan, 12.3451, false)]
    [InlineData(DrawingFilterOperator.GreaterThan, 12.3450, true)]
    [InlineData(DrawingFilterOperator.GreaterThanOrEqual, 12.3451, true)]
    [InlineData(DrawingFilterOperator.GreaterThanOrEqual, 12.3452, false)]
    public void NumericComparisonsKeepRawPrecision(DrawingFilterOperator comparison, double expected, bool matches)
    {
        var filter = new DrawingPropertyFilter(
            DrawingPropertyId.Length,
            comparison,
            new DrawingNumberValue(expected, DrawingUnit.Distance));

        Assert.Equal(matches, filter.Matches(new DrawingNumberValue(12.3451, DrawingUnit.Distance)));
    }

    /// <summary>Missing, nonfinite, and incompatible values do not enter a result through inequality.</summary>
    [Theory]
    [InlineData(DrawingFilterOperator.Equal)]
    [InlineData(DrawingFilterOperator.NotEqual)]
    [InlineData(DrawingFilterOperator.GreaterThan)]
    public void UnavailableAndIncompatibleNumbersNeverMatch(DrawingFilterOperator comparison)
    {
        var filter = new DrawingPropertyFilter(
            DrawingPropertyId.Length,
            comparison,
            new DrawingNumberValue(1, DrawingUnit.Distance));

        Assert.False(filter.Matches(null));
        Assert.False(filter.Matches(new DrawingTextValue("1")));
        Assert.False(filter.Matches(new DrawingNumberValue(2, DrawingUnit.Count)));

        foreach (var unavailable in new[] {double.NaN, double.PositiveInfinity, double.NegativeInfinity})
        {
            Assert.False(filter.Matches(new DrawingNumberValue(unavailable, DrawingUnit.Distance)));
            Assert.False((filter with {Value = new DrawingNumberValue(unavailable, DrawingUnit.Distance)})
                .Matches(new DrawingNumberValue(1, DrawingUnit.Distance)));
        }
    }

    /// <summary>User-entered text comparisons ignore case without treating application captions as values.</summary>
    [Fact]
    public void TextUsesOrdinalCaseInsensitiveMatching()
    {
        var filter = new DrawingPropertyFilter(
            DrawingPropertyId.BlockName,
            DrawingFilterOperator.Contains,
            new DrawingTextValue("вал"));

        Assert.True(filter.Matches(new DrawingTextValue("ВАЛ-01")));
        Assert.False(filter.Matches(new DrawingTextValue("WALL")));
        filter = filter with {Operator = DrawingFilterOperator.Equal};
        Assert.True(filter.Matches(new DrawingTextValue("ВАЛ")));
        Assert.False(filter.Matches(new DrawingTextValue("ВАЛ-01")));
        Assert.False((filter with {Operator = DrawingFilterOperator.LessThan}).Matches(new DrawingTextValue("А")));
    }

    /// <summary>Boolean state and layer identifiers remain independent of display captions.</summary>
    [Fact]
    public void BooleansAndLayersUseTheirTypedIdentity()
    {
        var open = new DrawingPropertyFilter(
            DrawingPropertyId.Closed,
            DrawingFilterOperator.Equal,
            new DrawingBooleanValue(false));
        var layer = new DrawingPropertyFilter(
            DrawingPropertyId.Layer,
            DrawingFilterOperator.Equal,
            new DrawingLayerValue(new TestLayerId("roads"), "Roads"));

        Assert.True(open.Matches(new DrawingBooleanValue(false)));
        Assert.False(open.Matches(new DrawingBooleanValue(true)));
        Assert.False(open.Matches(new DrawingTextValue("No", true)));
        Assert.True(layer.Matches(new DrawingLayerValue(new TestLayerId("roads"), "Renamed roads")));
        Assert.False(layer.Matches(new DrawingLayerValue(new TestLayerId("other"), "Roads")));
    }

    /// <summary>Assigned inheritance modes stay distinct from explicit appearance with the same payload.</summary>
    [Fact]
    public void AssignedValuesKeepNativeIdentity()
    {
        (DrawingPropertyId Property, DrawingValue Inherited, DrawingValue Explicit)[] values =
        [
            (DrawingPropertyId.Color,
                new DrawingColorValue(new AssignedColor(AssignedColorKind.ByLayer)),
                new DrawingColorValue(new AssignedColor(AssignedColorKind.Index))),
            (DrawingPropertyId.Lineweight,
                new DrawingLineweightValue(new AssignedLineweight(AssignedLineweightKind.ByLayer)),
                new DrawingLineweightValue(new AssignedLineweight(AssignedLineweightKind.Explicit))),
            (DrawingPropertyId.Transparency,
                new DrawingTransparencyValue(new AssignedTransparency(AssignedTransparencyKind.ByLayer)),
                new DrawingTransparencyValue(new AssignedTransparency(AssignedTransparencyKind.Explicit)))
        ];

        foreach (var (property, inherited, explicitValue) in values)
        {
            var filter = new DrawingPropertyFilter(property, DrawingFilterOperator.Equal, inherited);

            Assert.True(filter.Matches(inherited));
            Assert.False(filter.Matches(explicitValue));
            Assert.True((filter with {Operator = DrawingFilterOperator.NotEqual}).Matches(explicitValue));
            Assert.False((filter with {Operator = DrawingFilterOperator.Contains}).Matches(inherited));
        }
    }

    /// <summary>One type filter applies across layers before grouping and limits every parent target list.</summary>
    [Theory]
    [InlineData(DrawingGrouping.Layers)]
    [InlineData(DrawingGrouping.ObjectTypes)]
    public void FilteredGroupsPartitionOnlyMatchingTargets(DrawingGrouping grouping)
    {
        var inventory = Inventory();
        var presentation = Build(inventory, grouping, DrawingFilterOperator.LessThan, 10);
        var types = GetTypes(presentation, grouping).ToList();
        var polylines = types.Where(type => type.TypeKey == PolylineType).ToList();

        Assert.Same(inventory, presentation.Inventory);
        Assert.Equal(6, inventory.Entities.Length);
        Assert.Equal(3, presentation.Groups.Sum(group => group.Count));
        Assert.Equal(["1", "3", "5"], presentation.Groups.SelectMany(group => group.Objects)
            .Select(id => id.DisplayId).OrderBy(id => id));
        Assert.Equal(["1", "3"], polylines.SelectMany(type => type.Objects).Select(id => id.DisplayId));
        Assert.Single(types.Where(type => type.TypeKey == "AcDbLine").SelectMany(type => type.Objects));

        foreach (var type in polylines)
        {
            Assert.Equal(DrawingPropertyId.Length, type.RowMetric!.Id);
            Assert.All(type.Children, child => Assert.Equal(LensNodeKind.PropertyGroup, child.Kind));
            Assert.Equal(type.Objects, type.Children.SelectMany(child => child.Objects));
            Assert.All(
                type.Children,
                child => Assert.Equal(child.Objects, child.Children.SelectMany(node => node.Objects)));
        }

        if (grouping == DrawingGrouping.Layers)
            Assert.All(
                presentation.Groups,
                group => Assert.Equal(group.Objects, group.Children.SelectMany(type => type.Objects)));

        var cleared = DrawingLensProvider.Build(inventory, grouping, []);

        Assert.Equal(5, cleared.Groups.Sum(group => group.Count));
    }

    /// <summary>A condition matching nothing preserves its type location so the user can clear it.</summary>
    [Theory]
    [InlineData(DrawingGrouping.Layers)]
    [InlineData(DrawingGrouping.ObjectTypes)]
    public void EmptyFilteredTypesRemainNavigableWithoutTargets(DrawingGrouping grouping)
    {
        var presentation = Build(Inventory(), grouping, DrawingFilterOperator.GreaterThan, 100);
        var polylines = GetTypes(presentation, grouping).Where(type => type.TypeKey == PolylineType).ToList();

        Assert.NotEmpty(polylines);
        Assert.All(polylines, type =>
        {
            Assert.Equal(0, type.Count);
            Assert.Empty(type.Objects);
            Assert.Empty(type.Children);
            Assert.Null(type.RowMetric);
        });
        Assert.Equal(1, presentation.Groups.Sum(group => group.Count));
        Assert.Equal("5", Assert.Single(presentation.Groups.SelectMany(group => group.Objects)).DisplayId);

        if (grouping == DrawingGrouping.Layers)
            Assert.Equal(0, presentation.Groups.Single(group => group.Id == "utilities").Count);
    }

    private static IEnumerable<LensNode> GetTypes(LensPresentation presentation, DrawingGrouping grouping) =>
        grouping == DrawingGrouping.Layers
            ? presentation.Groups.SelectMany(group => group.Children)
            : presentation.Groups;

    private static LensPresentation Build(
        DrawingInventory inventory,
        DrawingGrouping grouping,
        DrawingFilterOperator comparison,
        double threshold) => DrawingLensProvider.Build(
        inventory,
        grouping,
        [],
        new Dictionary<string, ImmutableArray<DrawingPropertyId>> {[PolylineType] = [DrawingPropertyId.Closed]},
        new Dictionary<string, DrawingPropertyFilter>
        {
            [PolylineType] = new(
                DrawingPropertyId.Length,
                comparison,
                new DrawingNumberValue(threshold, DrawingUnit.Distance))
        });

    private static DrawingInventory Inventory() => new(
        "Model",
        [
            new LayerSnapshot(new TestLayerId("roads"), "Roads", false, false, false, false),
            new LayerSnapshot(new TestLayerId("utilities"), "Utilities", false, false, false, false),
            new LayerSnapshot(new TestLayerId("hidden"), "Hidden", true, false, false, false)
        ],
        [
            Entity("1", "roads", PolylineType, 5),
            Entity("2", "roads", PolylineType, 15),
            Entity("3", "utilities", PolylineType, 7),
            Entity("4", "utilities", PolylineType, 20),
            Entity("5", "roads", "AcDbLine", 50),
            Entity("6", "hidden", PolylineType, 2)
        ]);

    private static EntitySnapshot Entity(string id, string layerId, string typeKey, double length) => new(
        new TestEntityId(id),
        new TestLayerId(layerId),
        typeKey,
        new Dictionary<DrawingPropertyId, DrawingValue?>
        {
            [DrawingPropertyId.Length] = new DrawingNumberValue(length, DrawingUnit.Distance),
            [DrawingPropertyId.Closed] = new DrawingBooleanValue(false)
        }.ToImmutableDictionary(),
        DrawingPropertyId.Length);
}
