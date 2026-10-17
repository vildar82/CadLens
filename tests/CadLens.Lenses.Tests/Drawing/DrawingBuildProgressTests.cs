using System.Collections.Immutable;
using CadLens.Common;
using Xunit;

namespace CadLens.Lenses.Tests;

/// <summary>
/// Checks cancellation and progress while rebuilding detached drawing groups.
/// </summary>
public sealed class DrawingBuildProgressTests
{
    /// <summary>
    /// Initial reads and block browsing report the detached building stage through the existing read progress sink.
    /// </summary>
    /// <param name="blockContents">Whether to read one block's contents instead of the whole space.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadedInventoryReportsBuildingStage(bool blockContents)
    {
        var source = new Source(CreateInventory(600));
        var provider = new DrawingLensProvider(source);
        var updates = new List<DrawingReadProgress>();
        var progress = new InlineReadProgress(updates.Add);

        var result = blockContents
            ? await provider.LoadBlockContentsAsync(new TestEntityId("block"), [], CancellationToken.None, progress)
            : await provider.LoadAsync(DrawingGrouping.Layers, [], null, CancellationToken.None, progress: progress);

        Assert.IsType<HostResult<LensPresentation>.Success>(result);
        Assert.Equal(1, source.ReadCount);
        Assert.Contains(updates, update => update.Stage == OperationStage.Reading);
        Assert.Contains(updates, update => update is {Stage: OperationStage.Building, ProcessedObjects: > 0 and < 600});
        Assert.Equal(new DrawingReadProgress(600, 600, 600, OperationStage.Building), updates.Last());
    }

    /// <summary>
    /// A condition that excludes every object remains cancellable before any object node is constructed.
    /// </summary>
    /// <param name="grouping">Root organization of the lens.</param>
    [Theory]
    [InlineData(DrawingGrouping.Layers)]
    [InlineData(DrawingGrouping.ObjectTypes)]
    public void AllExcludedPropertyFilterStillObservesCancellation(DrawingGrouping grouping)
    {
        var inventory = CreateInventory(1_024);
        using var cancellation = new CancellationTokenSource();
        var updates = new List<OperationProgress>();
        var progress = new InlineProgress(update =>
        {
            updates.Add(update);

            if (update.Completed > 0)
            {
                // ReSharper disable once AccessToDisposedClosure -- Build completes before source disposal.
                cancellation.Cancel();
            }
        });
        var filters = new Dictionary<string, DrawingPropertyFilter>
        {
            ["AcDbLine"] = new(
                DrawingPropertyId.Length,
                DrawingFilterOperator.LessThan,
                new DrawingNumberValue(0, DrawingUnit.Distance))
        };

        var exception = Assert.Throws<OperationCanceledException>(() => DrawingLensProvider.Build(
            inventory,
            grouping,
            [],
            propertyFilters: filters,
            cancellationToken: cancellation.Token,
            progress: progress));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.All(updates, update => Assert.Equal(OperationStage.Building, update.Stage));
        Assert.InRange(updates.Last().Completed, 1, inventory.Entities.Length - 1);
        Assert.DoesNotContain(updates, update => update.Completed == update.Total);
    }

    /// <summary>
    /// Progress is bounded and monotonic while composite groups preserve every collected object exactly once.
    /// </summary>
    /// <param name="grouping">Root organization of the lens.</param>
    [Theory]
    [InlineData(DrawingGrouping.Layers)]
    [InlineData(DrawingGrouping.ObjectTypes)]
    public void CompositeBuildReportsThrottledProgressAndKeepsAllTargets(DrawingGrouping grouping)
    {
        var inventory = CreateInventory(600);
        var updates = new List<OperationProgress>();
        var fields = new Dictionary<string, ImmutableArray<DrawingPropertyKey>>
        {
            ["AcDbLine"] = [DrawingPropertyId.Length]
        };

        var presentation = DrawingLensProvider.Build(
            inventory,
            grouping,
            [],
            fields,
            progress: new InlineProgress(updates.Add));

        Assert.Equal(600, presentation.Groups.Sum(group => group.Objects.Length));
        var type = grouping == DrawingGrouping.Layers
            ? Assert.Single(Assert.Single(presentation.Groups).Children)
            : Assert.Single(presentation.Groups);
        Assert.Equal(2, type.Children.Length);
        Assert.Equal(600, type.Children.Sum(group => group.Objects.Length));
        Assert.Equal(new OperationProgress(OperationStage.Building, 0, 600), updates.First());
        Assert.Equal(new OperationProgress(OperationStage.Building, 600, 600), updates.Last());
        Assert.InRange(updates.Count, 3, 5);
        Assert.Equal(updates.OrderBy(update => update.Completed), updates);
        Assert.All(updates, update => Assert.Equal(OperationStage.Building, update.Stage));
    }

    private static DrawingInventory CreateInventory(int count)
    {
        var layer = new TestLayerId("Layer");
        ImmutableArray<EntitySnapshot> entities =
        [
            .. Enumerable.Range(0, count).Select(index => new EntitySnapshot(
                new TestEntityId(index.ToString("D4")),
                layer,
                "AcDbLine",
                new Dictionary<DrawingPropertyKey, DrawingValue?>
                {
                    [DrawingPropertyId.Length] = new DrawingNumberValue(index % 2 + 1, DrawingUnit.Distance)
                }.ToImmutableDictionary()))
        ];

        return new DrawingInventory(
            "Model",
            [new LayerSnapshot(layer, "Layer", false, false, false, false)],
            entities);
    }

    private sealed class InlineProgress(Action<OperationProgress> report) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => report(value);
    }

    private sealed class InlineReadProgress(Action<DrawingReadProgress> report) : IProgress<DrawingReadProgress>
    {
        public void Report(DrawingReadProgress value) => report(value);
    }

    private sealed class Source(DrawingInventory inventory) : IDrawingInventorySource
    {
        internal int ReadCount { get; private set; }

        public Task<HostResult<DrawingInventory>> ReadAsync(
            ImmutableArray<IPlacedObjectId>? selectedObjects,
            CancellationToken cancellationToken,
            int? maximumObjects = null,
            bool includeBlockContents = false,
            IProgress<DrawingReadProgress>? progress = null)
        {
            ReadCount++;
            progress?.Report(new DrawingReadProgress(600, 600, 600));
            return Task.FromResult<HostResult<DrawingInventory>>(new HostResult<DrawingInventory>.Success(inventory));
        }
    }
}