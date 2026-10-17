using System.Collections.Immutable;
using CadLens.Common;
using CadLens.Lenses;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>
/// Keeps visible groups and their applied options consistent when a detached rebuild is canceled.
/// </summary>
[Collection("Language changes")]
public sealed class OptionCancellationTests
{
    /// <summary>
    /// Canceling before a new tree is published restores the options that produced the retained tree.
    /// </summary>
    /// <param name="operation">Option change to cancel while rebuilding.</param>
    [Theory]
    [InlineData("grouping")]
    [InlineData("apply")]
    [InlineData("clear")]
    [InlineData("inclusion")]
    public async Task CanceledBuildRestoresAppliedOptions(string operation)
    {
        using var cancellation = new CancellationTokenSource();
        var id = new CancelingId();
        var layer = new LayerId("Layer");
        var inventory = new DrawingInventory(
            "Model",
            [new LayerSnapshot(layer, "Layer", false, false, false, false)],
            [new EntitySnapshot(
                id,
                layer,
                "AcDbLine",
                new Dictionary<DrawingPropertyKey, DrawingValue?>
                {
                    [DrawingPropertyId.Length] = new DrawingNumberValue(1, DrawingUnit.Distance)
                }.ToImmutableDictionary())]);
        var actions = new PropertyFilterTests.Actions {InventoryOverride = inventory};
        using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.ObjectTypes);
        await model.ActivateAsync(cancellation.Token);
        await model.EnterCommand.ExecuteAsync(Assert.Single(model.Items));
        model.PropertyFilter.PropertyId = DrawingPropertyId.Length;
        model.PropertyFilter.Operator = DrawingFilterOperator.GreaterThan;
        model.PropertyFilter.InputText = "0";

        if (operation == "clear")
            await model.ApplyPropertyFilterCommand.ExecuteAsync(null);

        var groups = model.Groups;
        var current = model.Current;
        var applied = model.AppliedPropertyFilter;
        var grouping = model.GroupingSummary;
        var filters = model.Filters;
        id.Cancellation = cancellation;

        switch (operation)
        {
            case "grouping":
                await model.ToggleGroupingCommand.ExecuteAsync(
                    model.GroupingOptions.Single(option => option.Id == DrawingPropertyId.Length));
                break;
            case "apply":
                await model.ApplyPropertyFilterCommand.ExecuteAsync(null);
                break;
            case "clear":
                await model.ClearPropertyFilterCommand.ExecuteAsync(null);
                break;
            case "inclusion":
                await model.ToggleFilterCommand.ExecuteAsync(model.Filters[0]);
                break;
        }

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(groups, model.Groups);
        Assert.Same(current, model.Current);
        Assert.Equal(applied, model.AppliedPropertyFilter);
        Assert.Equal(grouping, model.GroupingSummary);
        Assert.Equal(filters, model.Filters);
        Assert.Equal(1, actions.ReadCount);
    }

    private sealed record LayerId(string DisplayId) : ILayerId;

    private sealed class CancelingId : IPlacedObjectId
    {
        internal CancellationTokenSource? Cancellation { get; set; }

        public string DisplayId
        {
            get
            {
                Cancellation?.Cancel();
                return "1";
            }
        }
    }
}