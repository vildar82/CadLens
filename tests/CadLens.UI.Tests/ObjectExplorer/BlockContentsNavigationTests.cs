using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;
using CadLens.Common;
using CadLens.Lenses;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>
/// Exercises block scope navigation and occurrence actions without a native drawing.
/// </summary>
[Collection("Language changes")]
public sealed class BlockContentsNavigationTests
{
    /// <summary>
    /// Contents preserve the parent breadcrumb and occurrence targets before restoring the original object position.
    /// </summary>
    [Fact]
    public async Task BrowseContentsPreservesParentAndOccurrenceActions()
    {
        var actions = new Actions();
        using var model = await OpenBlock(actions);
        var block = model.Current!;
        var path = model.Breadcrumbs.ToArray();
        var position = model.ObjectPosition;
        var parentSearch = model.Groups.Single().Label;
        model.SearchText = parentSearch;

        await model.BrowseBlockContentsCommand.ExecuteAsync(null);

        Assert.True(model.IsBrowsingBlockContents);
        Assert.Null(model.Current);
        Assert.False(model.HasCurrent);
        Assert.True(model.HasNavigation);
        Assert.Empty(model.SearchText);
        Assert.Equal<LensNode>(path, model.Breadcrumbs);
        Assert.Equal("AcDbLine", Assert.Single(model.Items).TypeKey);
        Assert.False(model.CanBrowseBlockContents);
        model.SearchText = Assert.Single(model.Items).Label;
        await model.EnterCommand.ExecuteAsync(Assert.Single(model.Items));
        await model.EnterCommand.ExecuteAsync(Assert.Single(model.Items));
        Assert.Equal(Actions.Child, Assert.Single(model.Current!.Objects));
        await model.FocusCommand.ExecuteAsync(null);
        await model.SelectCommand.ExecuteAsync(null);
        await model.IsolateCommand.ExecuteAsync(null);
        Assert.Equal<IPlacedObjectId>([Actions.Child], actions.Focused);
        Assert.Equal<IPlacedObjectId>([Actions.Child], actions.Selected);
        Assert.Equal<IPlacedObjectId>([Actions.Child], actions.Isolated);

        await model.BackCommand.ExecuteAsync(null);
        await model.BackCommand.ExecuteAsync(null);
        Assert.True(model.IsBrowsingBlockContents);
        Assert.Null(model.Current);
        await model.BackCommand.ExecuteAsync(null);

        Assert.False(model.IsBrowsingBlockContents);
        Assert.Equal(block.Id, model.Current!.Id);
        Assert.Equal(parentSearch, model.SearchText);
        Assert.Equal(position, model.ObjectPosition);
        await model.NextCommand.ExecuteAsync(null);
        Assert.Equal("3", Assert.Single(model.Current!.Objects).DisplayId);
        await model.PreviousCommand.ExecuteAsync(null);
        Assert.Equal(Actions.Block, Assert.Single(model.Current!.Objects));
        await model.BrowseBlockContentsCommand.ExecuteAsync(null);
        await model.BreadcrumbCommand.ExecuteAsync(model.Breadcrumbs[^1]);
        Assert.False(model.IsBrowsingBlockContents);
        Assert.Equal(Actions.Block, Assert.Single(model.Current!.Objects));
        Assert.Equal(position, model.ObjectPosition);
    }

    /// <summary>
    /// Refresh retains the block target without prompting, and drawing scope commands leave its contents.
    /// </summary>
    /// <param name="showAll">Whether the all-objects control exits the block instead of the root breadcrumb.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefreshReadsSameBlockAndDrawingCommandsExitScope(bool showAll)
    {
        var actions = new Actions();
        using var model = await OpenBlock(actions);
        await model.BrowseBlockContentsCommand.ExecuteAsync(null);

        await model.ReadCommand.ExecuteAsync(null);

        Assert.Equal<IPlacedObjectId>([Actions.Block, Actions.Block], actions.BlockReads);
        Assert.Equal(1, actions.DrawingReads);
        Assert.Equal(0, actions.SelectionRequests);
        Assert.True(model.IsBrowsingBlockContents);
        Assert.Equal("Refresh block contents", model.RefreshLabel);

        if (showAll)
            await model.ShowAllObjectsCommand.ExecuteAsync(null);
        else
            await model.RootCommand.ExecuteAsync(null);

        Assert.False(model.IsBrowsingBlockContents);
        Assert.True(model.IsAllObjects);
        Assert.Equal(3, model.ObjectCount);
        Assert.Equal(showAll ? 2 : 1, actions.DrawingReads);
        Assert.Equal(0, actions.SelectionRequests);
    }

    /// <summary>
    /// A failed read or cleanup keeps the current parent object and its navigation position.
    /// </summary>
    /// <param name="cleanupFails">Whether cleanup stops the operation before reading the block.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnavailableContentsOrCleanupRetainsParent(bool cleanupFails)
    {
        var actions = new Actions();
        using var model = await OpenBlock(actions);
        var parent = model.Current;
        var position = model.ObjectPosition;
        actions.CleanupFails = cleanupFails;
        actions.ReadUnavailable = !cleanupFails;

        await model.BrowseBlockContentsCommand.ExecuteAsync(null);

        Assert.False(model.IsBrowsingBlockContents);
        Assert.Same(parent, model.Current);
        Assert.Equal(position, model.ObjectPosition);
        Assert.Equal(cleanupFails ? 0 : 1, actions.BlockReads.Count);
        Assert.True(model.BrowseBlockContentsCommand.CanExecute(null));
    }

    /// <summary>
    /// A late block read cannot repopulate the explorer after its drawing context is cleared.
    /// </summary>
    [Fact]
    public async Task ContextResetCancelsPendingBlockRead()
    {
        var actions = new Actions();
        using var model = await OpenBlock(actions);
        actions.Pending = new TaskCompletionSource<HostResult<LensPresentation>>();
        var browse = model.BrowseBlockContentsCommand.ExecuteAsync(null);
        Assert.True(model.IsBusy);

        await model.ResetContextAsync(false);
        actions.Pending.SetResult(new HostResult<LensPresentation>.Success(Actions.ContentsPresentation([])));
        await browse;

        Assert.True(actions.BlockReadToken.IsCancellationRequested);
        Assert.False(model.IsBrowsingBlockContents);
        Assert.Null(model.Current);
        Assert.Empty(model.Groups);
        Assert.Empty(model.Breadcrumbs);
        Assert.False(model.BrowseBlockContentsCommand.CanExecute(null));
    }

    /// <summary>
    /// Inclusion and property grouping rebuild the contents inventory without reading the drawing or block again.
    /// </summary>
    [Fact]
    public async Task InclusionAndGroupingUseRetainedBlockContents()
    {
        var actions = new Actions();
        using var model = await OpenBlock(actions);
        await model.BrowseBlockContentsCommand.ExecuteAsync(null);
        Assert.Equal(1, model.ObjectCount);

        await model.ToggleFilterCommand.ExecuteAsync(
            model.Filters.Single(option => option.Descriptor.Id == DrawingLensProvider.IncludeOff));
        await model.EnterCommand.ExecuteAsync(Assert.Single(model.Items));
        await model.ToggleGroupingCommand.ExecuteAsync(
            model.GroupingOptions.Single(option => option.Id == DrawingPropertyId.Layer));

        Assert.True(model.IsBrowsingBlockContents);
        Assert.Equal(2, model.Items.Length);
        Assert.Equal<IPlacedObjectId>(
            [Actions.Child, Actions.HiddenChild],
            model.Items.SelectMany(node => node.Objects).OrderBy(id => id.DisplayId));
        Assert.Equal(1, actions.DrawingReads);
        Assert.Single(actions.BlockReads);
        Assert.Equal(0, actions.SelectionRequests);
    }

    /// <summary>
    /// The real detail button follows the capability binding and updates its English and Russian label.
    /// </summary>
    [Fact]
    public void BrowseButtonBindsVisibilityCommandAndLocalizedLabel() => WpfTest.Run(() =>
    {
        var previous = UiText.Current.Preference;

        try
        {
            UiText.Current.Select(LanguagePreference.English, persist: false);
            var actions = new Actions();
            using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.ObjectTypes);
            model.ActivateAsync(CancellationToken.None).GetAwaiter().GetResult();
            var view = new ObjectExplorerView(model);
            view.Measure(new Size(320, 600));
            view.Arrange(new Rect(0, 0, 320, 600));
            view.UpdateLayout();
            var button = Assert.IsType<Button>(view.FindName("BrowseBlockContentsAction"));
            Assert.Equal(Visibility.Collapsed, button.Visibility);
            EnterBlock(model).GetAwaiter().GetResult();
            view.UpdateLayout();

            Assert.Equal(Visibility.Visible, button.Visibility);
            Assert.Same(model.BrowseBlockContentsCommand, button.Command);
            Assert.Equal("Browse block contents", button.Content);
            UiText.Current.Select(LanguagePreference.Russian, persist: false);
            Assert.Equal("Обзор объектов внутри блока", button.Content);
            model.BrowseBlockContentsCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            view.UpdateLayout();
            Assert.Equal(Visibility.Collapsed, button.Visibility);
        }
        finally
        {
            UiText.Current.Select(previous, persist: false);
        }
    });

    private static async Task<ObjectExplorerViewModel> OpenBlock(Actions actions)
    {
        var model = new ObjectExplorerViewModel(actions, DrawingGrouping.ObjectTypes);
        await model.ActivateAsync(CancellationToken.None);
        await EnterBlock(model);

        return model;
    }

    private static async Task EnterBlock(ObjectExplorerViewModel model)
    {
        await model.EnterCommand.ExecuteAsync(Assert.Single(model.Items));
        await model.EnterCommand.ExecuteAsync(model.Items.Single(node => node.Objects.Contains(Actions.Block)));
    }

    private sealed class Actions : IObjectExplorerActions
    {
        internal static readonly IPlacedObjectId Block = new TestEntityId(2);
        internal static readonly IPlacedObjectId Child = new OccurrenceId("2/A/B");
        internal static readonly IPlacedObjectId HiddenChild = new OccurrenceId("2/A/C");
        private static readonly LayerId VisibleLayer = new("visible");
        private static readonly LayerId OffLayer = new("off");
        private static readonly ImmutableArray<LayerSnapshot> Layers =
        [
            new(VisibleLayer, "Visible", false, false, false, false),
            new(OffLayer, "Off", true, false, false, false)
        ];

        private static readonly DrawingInventory Drawing = new(
            "Model",
            Layers,
            [.. Enumerable.Range(1, 3).Select(number => new EntitySnapshot(
                new TestEntityId(number),
                VisibleLayer,
                "AcDbBlockReference",
                CanBrowseContents: true))]);

        private static readonly DrawingInventory Contents = new(
            "Model",
            Layers,
            [new EntitySnapshot(Child, VisibleLayer, "AcDbLine"),
                new EntitySnapshot(HiddenChild, OffLayer, "AcDbLine")]);

        internal List<IPlacedObjectId> BlockReads { get; } = [];
        internal int DrawingReads { get; private set; }
        internal int SelectionRequests { get; private set; }
        internal bool CleanupFails { get; set; }
        internal bool ReadUnavailable { get; set; }
        internal CancellationToken BlockReadToken { get; private set; }
        internal TaskCompletionSource<HostResult<LensPresentation>>? Pending { get; set; }
        internal ImmutableArray<IPlacedObjectId> Selected { get; private set; } = [];
        internal ImmutableArray<IPlacedObjectId> Focused { get; private set; } = [];
        internal ImmutableArray<IPlacedObjectId> Isolated { get; private set; } = [];

        internal static LensPresentation ContentsPresentation(IReadOnlyCollection<string> filters) =>
            DrawingLensProvider.Build(Contents, DrawingGrouping.ObjectTypes, filters);

        /// <inheritdoc />
        public Task<HostResult<LensPresentation>> ReadAsync(
            DrawingGrouping grouping,
            IReadOnlyCollection<string> enabledFilters,
            ImmutableArray<IPlacedObjectId>? selectedObjects,
            CancellationToken cancellationToken,
            int? maximumObjects = null)
        {
            DrawingReads++;

            return Task.FromResult<HostResult<LensPresentation>>(new HostResult<LensPresentation>.Success(
                DrawingLensProvider.Build(Drawing, grouping, enabledFilters)));
        }

        /// <inheritdoc />
        public Task<HostResult<LensPresentation>> ReadBlockContentsAsync(
            IPlacedObjectId block,
            IReadOnlyCollection<string> enabledFilters,
            CancellationToken cancellationToken)
        {
            BlockReads.Add(block);
            BlockReadToken = cancellationToken;

            return Pending?.Task ?? Task.FromResult<HostResult<LensPresentation>>(ReadUnavailable
                ? new HostResult<LensPresentation>.Unavailable("Block contents are unavailable.")
                : new HostResult<LensPresentation>.Success(ContentsPresentation(enabledFilters)));
        }

        /// <inheritdoc />
        public Task<HostResult<ImmutableArray<IPlacedObjectId>>> RequestObjectsAsync(
            CancellationToken cancellationToken)
        {
            SelectionRequests++;

            return Task.FromResult<HostResult<ImmutableArray<IPlacedObjectId>>>(
                new HostResult<ImmutableArray<IPlacedObjectId>>.Success([Block]));
        }

        /// <inheritdoc />
        public Task<HostResult<bool>> ClearAsync(CancellationToken cancellationToken) => Success(!CleanupFails);

        /// <inheritdoc />
        public void ClearImmediately(bool hostTerminating)
        {
            Selected = [];
            Isolated = [];
        }

        /// <inheritdoc />
        public Task<HostResult<bool>> SelectAsync(
            ImmutableArray<IPlacedObjectId> objects,
            CancellationToken cancellationToken)
        {
            Selected = objects;

            return Success();
        }

        /// <inheritdoc />
        public Task<HostResult<bool>> ClearIsolationAsync(CancellationToken cancellationToken)
        {
            Isolated = [];

            return Success();
        }

        /// <inheritdoc />
        public Task<string> IsolateObjectsAsync(
            ImmutableArray<IPlacedObjectId> objects,
            CancellationToken cancellationToken)
        {
            Isolated = objects;

            return Task.FromResult("Isolated.");
        }

        /// <inheritdoc />
        public Task<string> FocusAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken)
        {
            Focused = objects;

            return Task.FromResult("View fitted.");
        }

        private static Task<HostResult<bool>> Success(bool result = true) =>
            Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(result));
    }

    private sealed record LayerId(string DisplayId) : ILayerId;
    private sealed record OccurrenceId(string DisplayId) : IPlacedObjectId;
}