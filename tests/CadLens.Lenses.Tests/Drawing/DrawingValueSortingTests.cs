using System.Collections.Immutable;
using Xunit;

namespace CadLens.Lenses.Tests;

/// <summary>Chosen-property sorting compares native values independently of display formatting.</summary>
public sealed class DrawingValueSortingTests
{
    /// <summary>Numbers compare numerically while raw text, booleans, and assigned modes retain deterministic order.</summary>
    [Fact]
    public void TypedValuesUseTheirNativeOrdering()
    {
        Assert.True(
            DrawingProperties.CompareValues(
                new DrawingNumberValue(2, DrawingUnit.Count),
                new DrawingNumberValue(10, DrawingUnit.Count)) < 0);
        Assert.True(DrawingProperties.CompareValues(new DrawingTextValue("Alpha"), new DrawingTextValue("beta")) < 0);
        Assert.True(DrawingProperties.CompareValues(new DrawingTextValue("Alpha"), new DrawingTextValue("alpha")) < 0);
        Assert.Equal(
            0,
            DrawingProperties.CompareValues(new DrawingTextValue("Yes"), new DrawingTextValue("Yes", true)));
        Assert.True(DrawingProperties.CompareValues(new DrawingBooleanValue(false), new DrawingBooleanValue(true)) < 0);
        Assert.True(
            DrawingProperties.CompareValues(
                new DrawingLayerValue(new TestLayerId("2"), "Alpha"),
                new DrawingLayerValue(new TestLayerId("1"), "Beta")) < 0);
        Assert.True(
            DrawingProperties.CompareValues(
                new DrawingColorValue(new AssignedColor(AssignedColorKind.ByLayer)),
                new DrawingColorValue(new AssignedColor(AssignedColorKind.Index, 1))) < 0);
        Assert.True(
            DrawingProperties.CompareValues(
                new DrawingColorValue(new AssignedColor(AssignedColorKind.Index, 2)),
                new DrawingColorValue(new AssignedColor(AssignedColorKind.Index, 10))) < 0);
        Assert.True(
            DrawingProperties.CompareValues(
                new DrawingLineweightValue(new AssignedLineweight(AssignedLineweightKind.Explicit, 25)),
                new DrawingLineweightValue(new AssignedLineweight(AssignedLineweightKind.Explicit, 100))) < 0);
        Assert.True(
            DrawingProperties.CompareValues(
                new DrawingTransparencyValue(new AssignedTransparency(AssignedTransparencyKind.Explicit, 2)),
                new DrawingTransparencyValue(new AssignedTransparency(AssignedTransparencyKind.Explicit, 10))) > 0);
    }

    /// <summary>Ascending transparency follows the displayed amount of transparency, from opaque to transparent.</summary>
    [Fact]
    public void TransparencySortsOpaqueBeforeTransparent()
    {
        var opaque = new DrawingTransparencyValue(new AssignedTransparency(AssignedTransparencyKind.Explicit));
        var transparent = new DrawingTransparencyValue(new AssignedTransparency(AssignedTransparencyKind.Explicit, 0));

        Assert.True(DrawingProperties.CompareValues(opaque, transparent) < 0);
        Assert.True(DrawingProperties.CompareValues(transparent, opaque) > 0);
        Assert.True(
            DrawingProperties.CompareValues(
                new DrawingTransparencyValue(new AssignedTransparency(AssignedTransparencyKind.ByLayer)),
                opaque) < 0);
    }

    /// <summary>Unit and value kinds break mixed-value ties while invalid measurements remain unavailable.</summary>
    [Fact]
    public void MixedKindsAndUnavailableValuesRemainDeterministic()
    {
        var number = new DrawingNumberValue(10, DrawingUnit.Count);
        Assert.True(DrawingProperties.CompareValues(number, new DrawingNumberValue(2, DrawingUnit.Distance)) < 0);
        Assert.True(DrawingProperties.CompareValues(number, new DrawingTextValue("2")) < 0);
        Assert.True(DrawingProperties.CompareValues(number, null) < 0);
        Assert.True(DrawingProperties.CompareValues(null, number) > 0);
        Assert.Equal(
            0,
            DrawingProperties.CompareValues(null, new DrawingNumberValue(double.PositiveInfinity, DrawingUnit.Count)));
        Assert.Equal(
            0,
            DrawingProperties.CompareValues(
                new DrawingNumberValue(-0d, DrawingUnit.Count),
                new DrawingNumberValue(0, DrawingUnit.Count)));
    }

    /// <summary>Object rows expose every observed property and assigned layer to the chosen-column consumers.</summary>
    [Fact]
    public void ObjectRowsPublishObservedValuesAndLayer()
    {
        var layer = new LayerSnapshot(new TestLayerId("roads"), "Roads", false, false, false, false);
        var entity = new EntitySnapshot(
            new TestEntityId("1"),
            layer.Id,
            "Custom.Object",
            new Dictionary<DrawingPropertyKey, DrawingValue?>
            {
                [DrawingPropertyId.Text] = new DrawingTextValue("Drawing-owned name"),
                [DrawingPropertyId.Length] = new DrawingNumberValue(12, DrawingUnit.Distance),
                [DrawingPropertyId.Width] = null
            }.ToImmutableDictionary(),
            DrawingPropertyId.Length);
        var presentation = DrawingLensProvider.Build(
            new DrawingInventory("Model", [layer], [entity]),
            DrawingGrouping.ObjectTypes,
            new HashSet<string>());
        var node = presentation.Groups[0].Children[0];

        Assert.Equal(
            new DrawingTextValue("Drawing-owned name"),
            DrawingProperties.GetValue(node, DrawingPropertyId.Text));
        Assert.Equal(
            new DrawingNumberValue(12, DrawingUnit.Distance),
            DrawingProperties.GetValue(node, DrawingPropertyId.Length));
        Assert.Equal(
            new DrawingLayerValue(layer.Id, layer.Name),
            DrawingProperties.GetValue(node, DrawingPropertyId.Layer));
        Assert.Contains(node.Properties, property => property is {Id.BuiltIn: DrawingPropertyId.Width, Value: null});
        Assert.Null(DrawingProperties.GetValue(node, DrawingPropertyId.Width));
        Assert.Null(DrawingProperties.GetValue(node, DrawingPropertyId.Radius));
    }

    /// <summary>Drawing precision controls numeric group membership without changing raw object facts.</summary>
    [Fact]
    public void DisplayPrecisionGroupsRoundedMeasurementsWithoutRoundingObjectFacts()
    {
        var layer = new LayerSnapshot(new TestLayerId("roads"), "Roads", false, false, false, false);
        var firstEntity = new EntitySnapshot(
            new TestEntityId("1"),
            layer.Id,
            "AcDbLine",
            new Dictionary<DrawingPropertyKey, DrawingValue?>
            {
                [DrawingPropertyId.Length] = new DrawingNumberValue(12.3412, DrawingUnit.Distance),
                [DrawingPropertyId.PatternAngle] = new DrawingNumberValue(0.123456, DrawingUnit.Angle)
            }.ToImmutableDictionary(),
            DrawingPropertyId.Length);
        var secondEntity = firstEntity with
        {
            Id = new TestEntityId("2"),
            Properties = firstEntity.Properties!.SetItem(
                DrawingPropertyId.Length,
                new DrawingNumberValue(12.3448, DrawingUnit.Distance))
        };
        var precision = new DrawingPrecision(2, 1);
        var inventory = new DrawingInventory("Model", [layer], [firstEntity, secondEntity], precision);
        var grouping = new Dictionary<string, ImmutableArray<DrawingPropertyKey>>
            {["AcDbLine"] = [DrawingPropertyId.Length]};
        var first = DrawingLensProvider.Build(inventory, DrawingGrouping.ObjectTypes, new HashSet<string>(), grouping);
        var changed = DrawingLensProvider.Build(
            inventory with {Precision = new DrawingPrecision(6, 3)},
            DrawingGrouping.ObjectTypes,
            new HashSet<string>(),
            grouping);

        Assert.Same(precision, first.Precision);
        Assert.Same(precision, first.Inventory!.Precision);
        Assert.Equal(6, changed.Precision!.Linear);
        Assert.Equal(3, changed.Precision.Angular);
        Assert.Equal(2, Assert.Single(first.Groups[0].Children).Count);
        Assert.Equal(new DrawingNumberValue(12.34, DrawingUnit.Distance), first.Groups[0].Children[0].Properties[0].Value);
        Assert.Equal(2, changed.Groups[0].Children.Length);
        Assert.DoesNotContain(first.Groups[0].Children[0].Id, changed.Groups[0].Children.Select(node => node.Id));
        Assert.Equal(
            12.3412,
            Assert.IsType<DrawingNumberValue>(DrawingProperties.GetValue(firstEntity, layer, DrawingPropertyId.Length))
                .Value);
        Assert.Equal(
            0.123456,
            Assert.IsType<DrawingNumberValue>(
                DrawingProperties.GetValue(firstEntity, layer, DrawingPropertyId.PatternAngle)).Value);
    }

    /// <summary>A subgroup's metric header remains correct when its first member has no declared primary measurement.</summary>
    [Fact]
    public void SubgroupMetricDoesNotDependOnFirstMember()
    {
        var layer = new LayerSnapshot(new TestLayerId("roads"), "Roads", false, false, false, false);
        var unknown = new EntitySnapshot(new TestEntityId("1"), layer.Id, "Custom.Curve");
        var measured = new EntitySnapshot(
            new TestEntityId("2"),
            layer.Id,
            "Custom.Curve",
            new Dictionary<DrawingPropertyKey, DrawingValue?>
                {[DrawingPropertyId.Length] = new DrawingNumberValue(12, DrawingUnit.Distance)}.ToImmutableDictionary(),
            DrawingPropertyId.Length);
        var presentation = DrawingLensProvider.Build(
            new DrawingInventory("Model", [layer], [unknown, measured]),
            DrawingGrouping.ObjectTypes,
            new HashSet<string>(),
            new Dictionary<string, ImmutableArray<DrawingPropertyKey>> {["Custom.Curve"] = [DrawingPropertyId.Layer]});

        Assert.Equal(DrawingPropertyId.Length, presentation.Groups[0].RowMetric!.Id);
        Assert.Equal(DrawingPropertyId.Length, presentation.Groups[0].Children[0].RowMetric!.Id);
        Assert.Null(presentation.Groups[0].Children[0].Children[0].RowMetric);
    }
}