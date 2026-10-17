using System.Collections.Immutable;
using CadLens.Common;
using Xunit;

namespace CadLens.Lenses.Tests;

/// <summary>
/// Verifies placed block contents use the existing Object Types and visibility contracts.
/// </summary>
public sealed class BlockContentsTests
{
    private static readonly TestEntityId Block = new("insert");
    private static readonly LayerSnapshot Visible =
        new(new TestLayerId("visible"), "Visible", false, false, false, false);
    private static readonly LayerSnapshot Off = new(new TestLayerId("off"), "Off", true, false, false, false);
    private static readonly LayerSnapshot Frozen = new(new TestLayerId("frozen"), "Frozen", false, true, false, false);

    /// <summary>
    /// A single insertion is expanded without an automatic read limit and grouped by runtime type.
    /// </summary>
    [Fact]
    public async Task ReadsOnlyRequestedInsertionAndGroupsPlacedContents()
    {
        var source = new Source(new DrawingInventory(
            "Model",
            [Visible],
            [Entity("insert/line", "AcDbLine"), Entity("insert/nested/line", "AcDbLine"),
                Entity("insert/circle", "AcDbCircle")]));
        var provider = new DrawingLensProvider(source);
        using var cancellation = new CancellationTokenSource();
        var progress = new Progress<DrawingReadProgress>();

        var result = await provider.LoadBlockContentsAsync(Block, [], cancellation.Token, progress);

        Assert.Equal<IPlacedObjectId>([Block], source.SelectedObjects!.Value);
        Assert.True(source.IncludeBlockContents);
        Assert.Null(source.MaximumObjects);
        Assert.Equal(cancellation.Token, source.CancellationToken);
        Assert.Same(progress, source.Progress);
        var presentation = Assert.IsType<HostResult<LensPresentation>.Success>(result).Value;
        Assert.Equal("Object Types", presentation.Label);
        Assert.DoesNotContain(presentation.Filters, filter => filter.Id == DrawingLensProvider.ShowBlockContents);
        Assert.Equal(["AcDbCircle", "AcDbLine"], presentation.Groups.Select(node => node.Id));
        var lines = presentation.Groups[1];
        Assert.Equal(["insert/line", "insert/nested/line"], lines.Objects.Select(id => id.DisplayId));
        Assert.All(lines.Children, node => Assert.Equal(LensNodeKind.Object, node.Kind));
        Assert.Same(source.Inventory, presentation.Inventory);
    }

    /// <summary>
    /// Effective and containing layers control visibility for the selected insertion's contents.
    /// </summary>
    /// <param name="includeOff">Whether switched-off layers are included.</param>
    /// <param name="includeFrozen">Whether frozen layers are included.</param>
    /// <param name="expected">Expected placed identities in ordinal order.</param>
    [Theory]
    [InlineData(false, false, "insert/visible")]
    [InlineData(true, false, "insert/off-container,insert/off-leaf,insert/visible")]
    [InlineData(false, true, "insert/frozen-container,insert/visible")]
    [InlineData(true, true, "insert/frozen-container,insert/off-container,insert/off-leaf,insert/visible")]
    public async Task AppliesVisibilityFiltersToPlacedContents(bool includeOff, bool includeFrozen, string expected)
    {
        var source = new Source(new DrawingInventory(
            "Model",
            [Visible, Off, Frozen],
            [
                Entity("insert/visible", "AcDbLine"),
                Entity("insert/off-leaf", "AcDbCircle") with {LayerId = Off.Id},
                Entity("insert/off-container", "AcDbLine") with {ContainerLayers = [Off.Id]},
                Entity("insert/frozen-container", "AcDbLine") with {ContainerLayers = [Frozen.Id]}
            ]));
        var filters = new List<string>();

        if (includeOff)
            filters.Add(DrawingLensProvider.IncludeOff);

        if (includeFrozen)
            filters.Add(DrawingLensProvider.IncludeFrozen);

        var result = await new DrawingLensProvider(source)
            .LoadBlockContentsAsync(Block, filters, CancellationToken.None);

        var presentation = Assert.IsType<HostResult<LensPresentation>.Success>(result).Value;
        Assert.Equal(
            expected.Split(','),
            presentation.Groups.SelectMany(node => node.Objects).Select(id => id.DisplayId)
                .OrderBy(id => id, StringComparer.Ordinal));
    }

    /// <summary>
    /// Only object snapshots explicitly eligible for browsing expose the additional action.
    /// </summary>
    /// <param name="grouping">Lens organization.</param>
    [Theory]
    [InlineData(DrawingGrouping.Layers)]
    [InlineData(DrawingGrouping.ObjectTypes)]
    public void BrowseActionRequiresSnapshotCapability(DrawingGrouping grouping)
    {
        var inventory = new DrawingInventory(
            "Model",
            [Visible],
            [Entity("local", "AcDbBlockReference") with {CanBrowseContents = true},
                Entity("atomic", "AcDbBlockReference"), Entity("line", "AcDbLine")]);

        var presentation = DrawingLensProvider.Build(inventory, grouping, []);

        var types = grouping == DrawingGrouping.Layers
            ? presentation.Groups.SelectMany(node => node.Children)
            : presentation.Groups;
        var objects = types.SelectMany(node => node.Children).ToDictionary(node => node.Id);
        Assert.Equal<LensAction>([LensAction.Focus, LensAction.BrowseContents], objects["local"].Actions);
        Assert.Equal<LensAction>([LensAction.Focus], objects["atomic"].Actions);
        Assert.Equal<LensAction>([LensAction.Focus], objects["line"].Actions);
    }

    /// <summary>
    /// An insertion that can no longer expand is rejected even when visibility would hide it.
    /// </summary>
    [Fact]
    public async Task RejectsInsertionRetainedAsAtomicObject()
    {
        var source = new Source(new DrawingInventory(
            "Model",
            [Off],
            [new EntitySnapshot(Block, Off.Id, "AcDbBlockReference")]));

        var result = await new DrawingLensProvider(source)
            .LoadBlockContentsAsync(Block, [], CancellationToken.None);

        Assert.Equal(
            "Block contents are no longer available. Refresh to try again.",
            Assert.IsType<HostResult<LensPresentation>.Unavailable>(result).Reason);
    }

    private static EntitySnapshot Entity(string key, string type) =>
        new(new TestEntityId(key), Visible.Id, type);

    private sealed class Source(DrawingInventory inventory) : IDrawingInventorySource
    {
        internal DrawingInventory Inventory { get; } = inventory;
        internal ImmutableArray<IPlacedObjectId>? SelectedObjects { get; private set; }
        internal bool IncludeBlockContents { get; private set; }
        internal int? MaximumObjects { get; private set; }
        internal CancellationToken CancellationToken { get; private set; }
        internal IProgress<DrawingReadProgress>? Progress { get; private set; }

        public Task<HostResult<DrawingInventory>> ReadAsync(
            ImmutableArray<IPlacedObjectId>? selectedObjects,
            CancellationToken cancellationToken,
            int? maximumObjects = null,
            bool includeBlockContents = false,
            IProgress<DrawingReadProgress>? progress = null)
        {
            Progress = progress;
            SelectedObjects = selectedObjects;
            CancellationToken = cancellationToken;
            MaximumObjects = maximumObjects;
            IncludeBlockContents = includeBlockContents;
            return Task.FromResult<HostResult<DrawingInventory>>(new HostResult<DrawingInventory>.Success(Inventory));
        }
    }
}