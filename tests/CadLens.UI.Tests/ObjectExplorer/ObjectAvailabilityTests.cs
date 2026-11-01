using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;
using CadLens.Common;
using CadLens.Lenses;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>
/// Checks one displayed object while retaining its collected details and the surrounding inventory.
/// </summary>
[Collection("Language changes")]
public sealed class ObjectAvailabilityTests
{
    /// <summary>
    /// Missing objects retain every field and their position, and revisiting after Undo restores drawing actions.
    /// </summary>
    /// <param name="grouping">Lens organization.</param>
    [Theory]
    [InlineData(DrawingGrouping.Layers)]
    [InlineData(DrawingGrouping.ObjectTypes)]
    public async Task MissingObjectKeepsSnapshotAndRechecksOnlyNavigatedTargets(DrawingGrouping grouping)
    {
        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, grouping);
        await model.ActivateAsync(CancellationToken.None);
        Assert.Empty(actions.Checked);
        await OpenObjectListAsync(model, grouping);
        Assert.Empty(actions.Checked);
        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        var original = model.Current!;
        var fields = model.CurrentFields;
        var position = model.ObjectPosition;
        var groups = model.Groups;
        await model.BackCommand.ExecuteAsync(null);
        actions.Checked.Clear();
        actions.Results[First] = new HostResult<bool>.Success(false);
        await model.ToggleAutoSelectCommand.ExecuteAsync(null);
        await model.ToggleAutoIsolationCommand.ExecuteAsync(null);
        await model.ToggleAutoFocusCommand.ExecuteAsync(null);
        var focusCalls = actions.FocusCalls;
        var selectCalls = actions.NonEmptySelections;
        var isolateCalls = actions.NonEmptyIsolations;

        await model.EnterCommand.ExecuteAsync(original);

        Assert.Equal<IPlacedObjectId>([First], actions.Checked);
        Assert.Equal(1, actions.ReadCount);
        Assert.Equal(groups, model.Groups);
        Assert.Same(original, model.Current);
        Assert.Equal<DetailField>(fields, model.CurrentFields);
        Assert.Equal(position, model.ObjectPosition);
        Assert.True(model.IsCurrentObjectMissing);
        Assert.True(model.HasObjectAvailabilityNotice);
        Assert.Equal(UiText.Current.Get(MissingMessage), model.ObjectAvailabilityNotice);
        Assert.Empty(actions.Selected);
        Assert.Empty(actions.Isolated);
        Assert.Equal(focusCalls, actions.FocusCalls);
        Assert.Equal(selectCalls, actions.NonEmptySelections);
        Assert.Equal(isolateCalls, actions.NonEmptyIsolations);
        AssertDrawingActions(model, false);
        Assert.True(model.NextCommand.CanExecute(null));
        Assert.True(model.BackCommand.CanExecute(null));

        await model.NextCommand.ExecuteAsync(null);

        Assert.Equal(Second, Assert.Single(model.Current!.Objects));
        Assert.False(model.IsCurrentObjectMissing);
        Assert.False(model.HasObjectAvailabilityNotice);
        AssertDrawingActions(model, true);
        actions.Results[First] = new HostResult<bool>.Success(true);
        await model.PreviousCommand.ExecuteAsync(null);
        Assert.Same(original, model.Current);
        Assert.False(model.IsCurrentObjectMissing);
        Assert.False(model.HasObjectAvailabilityNotice);
        AssertDrawingActions(model, true);
        Assert.Equal<IPlacedObjectId>([First, Second, First], actions.Checked);
        Assert.Equal(1, actions.ReadCount);
    }

    /// <summary>
    /// An unavailable check preserves the snapshot without claiming that the object was deleted.
    /// </summary>
    [Fact]
    public async Task UnavailableCheckHasItsOwnNotice()
    {
        var actions = new Actions
        {
            Results = {[First] = new HostResult<bool>.Unavailable("Fixture verification unavailable.")}
        };
        using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.ObjectTypes);
        await model.ActivateAsync(CancellationToken.None);
        await OpenObjectListAsync(model, DrawingGrouping.ObjectTypes);
        var original = model.Items[0];

        await model.EnterCommand.ExecuteAsync(original);

        Assert.Same(original, model.Current);
        Assert.Contains(model.CurrentFields, field => field is {Label: "MARK", Value: "captured-1"});
        Assert.False(model.IsCurrentObjectMissing);
        Assert.True(model.HasObjectAvailabilityNotice);
        Assert.Contains("Fixture verification unavailable.", model.ObjectAvailabilityNotice);
        Assert.DoesNotContain(UiText.Current.Get(MissingMessage), model.ObjectAvailabilityNotice);
        AssertDrawingActions(model, false);
        Assert.Equal(1, actions.ReadCount);
        Assert.Empty(actions.Selected);
        Assert.Empty(actions.Isolated);
    }

    /// <summary>
    /// Late checks after a context change or panel close cannot publish a missing-object notice.
    /// </summary>
    /// <param name="changeContext">Whether a context reset cancels the check instead of closing the panel.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanceledCheckCannotPublishAvailability(bool changeContext)
    {
        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.ObjectTypes);
        await model.ActivateAsync(CancellationToken.None);
        await OpenObjectListAsync(model, DrawingGrouping.ObjectTypes);
        actions.PendingExistence = new TaskCompletionSource<HostResult<bool>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var navigation = model.EnterCommand.ExecuteAsync(model.Items[0]);
        Assert.True(model.IsBusy);

        if (changeContext)
            await model.ResetContextAsync(false);
        else
            model.Close(false);

        Assert.True(actions.ExistenceToken.IsCancellationRequested);
        actions.PendingExistence.SetResult(new HostResult<bool>.Success(false));
        await navigation;
        Assert.False(model.IsCurrentObjectMissing);
        Assert.False(model.HasObjectAvailabilityNotice);
        Assert.Empty(model.ObjectAvailabilityNotice);
        Assert.Equal(1, actions.ReadCount);
    }

    /// <summary>
    /// Reactivation checks the retained object without loading the drawing inventory again.
    /// </summary>
    [Fact]
    public async Task ReactivationChecksRetainedObject()
    {
        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.ObjectTypes);
        await model.ActivateAsync(CancellationToken.None);
        await OpenObjectListAsync(model, DrawingGrouping.ObjectTypes);
        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        var fields = model.CurrentFields;
        await model.DeactivateAsync(CancellationToken.None);
        actions.Results[First] = new HostResult<bool>.Success(false);

        await model.ActivateAsync(CancellationToken.None);

        Assert.True(model.IsCurrentObjectMissing);
        Assert.Equal<DetailField>(fields, model.CurrentFields);
        Assert.Equal<IPlacedObjectId>([First, First], actions.Checked);
        Assert.Equal(1, actions.ReadCount);
    }

    /// <summary>
    /// Returning from a block's contents rechecks its parent insertion without discarding its original details.
    /// </summary>
    [Fact]
    public async Task BackToParentObjectRechecksItsAvailability()
    {
        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.ObjectTypes);
        await model.ActivateAsync(CancellationToken.None);
        await OpenObjectListAsync(model, DrawingGrouping.ObjectTypes);
        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        var fields = model.CurrentFields;
        var position = model.ObjectPosition;
        await model.BrowseBlockContentsCommand.ExecuteAsync(null);
        actions.Results[First] = new HostResult<bool>.Success(false);

        await model.BackCommand.ExecuteAsync(null);

        Assert.False(model.IsBrowsingBlockContents);
        Assert.Equal(First, Assert.Single(model.Current!.Objects));
        Assert.Equal<DetailField>(fields, model.CurrentFields);
        Assert.Equal(position, model.ObjectPosition);
        Assert.True(model.IsCurrentObjectMissing);
        Assert.Equal<IPlacedObjectId>([First, First], actions.Checked);
        Assert.Equal(1, actions.ReadCount);
        Assert.Equal(1, actions.BlockReads);
    }

    /// <summary>
    /// The production detail view shows a localized notice alongside the unchanged captured attribute value.
    /// </summary>
    /// <param name="language">App display language.</param>
    [Theory]
    [InlineData(LanguagePreference.English)]
    [InlineData(LanguagePreference.Russian)]
    public void DetailNoticeKeepsCapturedValuesVisible(LanguagePreference language) => WpfTest.Run(() =>
    {
        var previous = UiText.Current.Preference;
        var actions = new Actions
        {
            Results = {[First] = new HostResult<bool>.Success(false)}
        };
        using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.ObjectTypes);

        try
        {
            UiText.Current.Select(language, persist: false);
            model.ActivateAsync(CancellationToken.None).GetAwaiter().GetResult();
            OpenObjectListAsync(model, DrawingGrouping.ObjectTypes).GetAwaiter().GetResult();
            model.EnterCommand.ExecuteAsync(model.Items[0]).GetAwaiter().GetResult();
            var view = new ObjectExplorerView(model);
            view.Measure(new Size(300, 660));
            view.Arrange(new Rect(0, 0, 300, 660));
            view.UpdateLayout();
            var notice = Assert.IsType<TextBlock>(view.FindName("ObjectAvailabilityNotice"));
            var browse = Assert.IsType<Button>(view.FindName("BrowseBlockContentsAction"));

            Assert.Equal(Visibility.Visible, notice.Visibility);
            Assert.Equal(UiText.Current.Get(MissingMessage), notice.Text);
            Assert.True(notice.ActualHeight > 0);
            Assert.Equal(TextWrapping.Wrap, notice.TextWrapping);
            Assert.Contains(WpfTest.Descendants(view).OfType<TextBlock>(), block => block.Text == "captured-1");
            Assert.Equal(Visibility.Visible, browse.Visibility);
            Assert.False(browse.IsEnabled);
        }
        finally
        {
            UiText.Current.Select(previous, persist: false);
        }
    });

    private const string MissingMessage = "Object no longer exists. Showing collected data.";
    private static readonly IPlacedObjectId First = new TestEntityId(1);
    private static readonly IPlacedObjectId Second = new TestEntityId(2);
    private static readonly LayerId Layer = new("Layer");

    private static async Task OpenObjectListAsync(ObjectExplorerViewModel model, DrawingGrouping grouping)
    {
        if (grouping == DrawingGrouping.Layers)
            await model.EnterCommand.ExecuteAsync(Assert.Single(model.Items));

        await model.EnterCommand.ExecuteAsync(Assert.Single(model.Items));
    }

    private static void AssertDrawingActions(ObjectExplorerViewModel model, bool enabled)
    {
        Assert.Equal(enabled, model.FocusCommand.CanExecute(null));
        Assert.Equal(enabled, model.SelectCommand.CanExecute(null));
        Assert.Equal(enabled, model.IsolateCommand.CanExecute(null));

        if (model.CanBrowseBlockContents)
            Assert.Equal(enabled, model.BrowseBlockContentsCommand.CanExecute(null));
    }

    private sealed record LayerId(string DisplayId) : ILayerId;

    private sealed class Actions : IObjectExplorerActions
    {
        public Task<HostResult<bool>> PaintAsync(
            ImmutableArray<PaintGroup> groups,
            CancellationToken cancellationToken,
            IProgress<OperationProgress>? progress = null) =>
            Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(true));

        internal Dictionary<IPlacedObjectId, HostResult<bool>> Results { get; } = [];
        internal List<IPlacedObjectId> Checked { get; } = [];
        internal TaskCompletionSource<HostResult<bool>>? PendingExistence { get; set; }
        internal CancellationToken ExistenceToken { get; private set; }
        internal int ReadCount { get; private set; }
        internal int BlockReads { get; private set; }
        internal int FocusCalls { get; private set; }
        internal int NonEmptySelections { get; private set; }
        internal int NonEmptyIsolations { get; private set; }
        internal ImmutableArray<IPlacedObjectId> Selected { get; private set; } = [];
        internal ImmutableArray<IPlacedObjectId> Isolated { get; private set; } = [];

        private static DrawingInventory Inventory => new(
            "Model",
            [new LayerSnapshot(Layer, "Layer", false, false, false, false)],
            [Block(First, "captured-1"), Block(Second, "captured-2")]);

        private static EntitySnapshot Block(IPlacedObjectId id, string mark) => new(
            id,
            Layer,
            "AcDbBlockReference",
            new Dictionary<DrawingPropertyKey, DrawingValue?>
            {
                [DrawingPropertyId.BlockName] = new DrawingTextValue("Equipment"),
                [DrawingPropertyId.Attributes] = new DrawingNumberValue(1, DrawingUnit.Count),
                [DrawingPropertyKey.ForAttribute("MARK")] = new DrawingTextValue(mark)
            }.ToImmutableDictionary(),
            CanBrowseContents: true);

        /// <inheritdoc />
        public Task<HostResult<bool>> ExistsAsync(
            IPlacedObjectId target,
            CancellationToken cancellationToken,
            IProgress<OperationProgress>? progress = null)
        {
            Checked.Add(target);
            ExistenceToken = cancellationToken;
            return PendingExistence?.Task ?? Task.FromResult(
                Results.TryGetValue(target, out var result) ? result : new HostResult<bool>.Success(true));
        }

        /// <inheritdoc />
        public Task<HostResult<LensPresentation>> ReadAsync(
            DrawingGrouping grouping,
            IReadOnlyCollection<string> enabledFilters,
            ImmutableArray<IPlacedObjectId>? selectedObjects,
            CancellationToken cancellationToken,
            int? maximumObjects = null,
            IProgress<DrawingReadProgress>? progress = null)
        {
            ReadCount++;
            return Task.FromResult<HostResult<LensPresentation>>(new HostResult<LensPresentation>.Success(
                DrawingLensProvider.Build(Inventory, grouping, enabledFilters)));
        }

        /// <inheritdoc />
        public Task<HostResult<LensPresentation>> ReadBlockContentsAsync(
            IPlacedObjectId block,
            IReadOnlyCollection<string> enabledFilters,
            CancellationToken cancellationToken,
            IProgress<DrawingReadProgress>? progress = null)
        {
            BlockReads++;
            var inventory = Inventory with {Entities = [new EntitySnapshot(new TestEntityId(3), Layer, "AcDbLine")]};
            return Task.FromResult<HostResult<LensPresentation>>(new HostResult<LensPresentation>.Success(
                DrawingLensProvider.Build(inventory, DrawingGrouping.ObjectTypes, enabledFilters)));
        }

        /// <inheritdoc />
        public Task<HostResult<ImmutableArray<IPlacedObjectId>>> RequestObjectsAsync(
            CancellationToken cancellationToken,
            IProgress<OperationProgress>? progress = null) =>
            Task.FromResult<HostResult<ImmutableArray<IPlacedObjectId>>>(
                new HostResult<ImmutableArray<IPlacedObjectId>>.Success([]));

        /// <inheritdoc />
        public void ClearImmediately(bool hostTerminating)
        {
            Selected = [];
            Isolated = [];
        }

        /// <inheritdoc />
        public Task<HostResult<bool>> SelectAsync(
            ImmutableArray<IPlacedObjectId> objects,
            CancellationToken cancellationToken,
            IProgress<OperationProgress>? progress = null)
        {
            Selected = objects;

            if (!objects.IsEmpty)
                NonEmptySelections++;

            return Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(true));
        }

        /// <inheritdoc />
        public Task<HostResult<bool>> ClearIsolationAsync(
            CancellationToken cancellationToken,
            IProgress<OperationProgress>? progress = null)
        {
            Isolated = [];
            return Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(true));
        }

        /// <inheritdoc />
        public Task<string> IsolateObjectsAsync(
            ImmutableArray<IPlacedObjectId> objects,
            CancellationToken cancellationToken,
            IProgress<OperationProgress>? progress = null)
        {
            Isolated = objects;

            if (!objects.IsEmpty)
                NonEmptyIsolations++;

            return Task.FromResult("Isolated.");
        }

        /// <inheritdoc />
        public Task<HostResult<bool>> ClearAsync(
            CancellationToken cancellationToken,
            IProgress<OperationProgress>? progress = null)
        {
            ClearImmediately(false);
            return Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(true));
        }

        /// <inheritdoc />
        public Task<string> FocusAsync(
            ImmutableArray<IPlacedObjectId> objects,
            CancellationToken cancellationToken,
            IProgress<OperationProgress>? progress = null)
        {
            FocusCalls++;
            return Task.FromResult("Focused.");
        }
    }
}