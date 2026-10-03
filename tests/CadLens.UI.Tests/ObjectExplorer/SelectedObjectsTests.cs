using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CadLens.Lenses;
using Common;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>Selection requests load immediately while retained scope stays detached from later CAD effects.</summary>
[Collection("Language changes")]
public sealed class SelectedObjectsTests
{
    /// <summary>Real controls reflect selected scope and update their refresh text when language changes.</summary>
    [Fact]
    public void ScopeControlsShowLocalizedSelectionAndRefresh() => WpfTest.Run(() =>
    {
        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.Layers);
        model.ActivateAsync(CancellationToken.None).GetAwaiter().GetResult();
        actions.NativeSelection = [new TestEntityId(1)];
        model.ShowSelectedObjectsCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        var view = new ObjectExplorerView(model);
        view.Measure(new Size(300, 450));
        view.Arrange(new Rect(0, 0, 300, 450));
        view.UpdateLayout();
        var selected = Assert.IsType<RadioButton>(view.FindName("SelectedObjectsScope"));
        var all = Assert.IsType<RadioButton>(view.FindName("AllObjectsScope"));
        var refresh = WpfTest.Descendants(view).OfType<Button>().Single(button => ReferenceEquals(button.Command, model.ReadCommand));
        var previous = UiText.Current.Preference;

        try
        {
            UiText.Current.Select(LanguagePreference.Russian, persist: false);
            Assert.True(selected.IsChecked);
            Assert.False(all.IsChecked);
            Assert.Equal("Выбранные объекты", selected.Content);
            Assert.Equal("Обновить выбранные объекты", refresh.ToolTip);
            Assert.Equal(
                "Использовать выбранные объекты CAD или выбрать объекты в CAD. Обновление заменяет набор объектов.",
                selected.ToolTip);
            var status = model.Status;
            model.ShowSelectedObjectsCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            Assert.Equal(status, model.Status);
            model.ShowAllObjectsCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            actions.RequestFailure = "Object selection canceled. Previous view kept.";
            selected.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            model.ShowSelectedObjectsCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            Assert.False(selected.IsChecked);
            Assert.True(all.IsChecked);
            Assert.Equal("Выбор объектов отменён. Предыдущий вид сохранён.", model.Status);
        }
        finally
        {
            UiText.Current.Select(previous, persist: false);
        }
    });

    /// <summary>Both lenses request objects before cleanup and keep scope during navigation and filter changes.</summary>
    [Theory]
    [InlineData(DrawingGrouping.Layers)]
    [InlineData(DrawingGrouping.ObjectTypes)]
    public async Task RequestsBeforeCleanupAndKeepsSnapshotUntilRefresh(DrawingGrouping grouping)
    {
        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, grouping);
        await model.ActivateAsync(CancellationToken.None);
        actions.NativeSelection = [new TestEntityId(1), new TestEntityId(3)];
        actions.Calls.Clear();
        await model.ShowSelectedObjectsCommand.ExecuteAsync(null);

        Assert.Equal(["request", "clear", "read"], actions.Calls);
        Assert.True(model.IsSelectedObjectsOnly);
        Assert.Equal("Refresh selected objects", model.RefreshLabel);
        Assert.Equal(1, model.ObjectCount);
        var reads = actions.ReadCount;
        var requests = actions.RequestCount;
        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        await model.ToggleAutoSelectCommand.ExecuteAsync(null);
        actions.NativeSelection = [new TestEntityId(2)];
        await model.RootCommand.ExecuteAsync(null);
        await model.ToggleFilterCommand.ExecuteAsync(model.Filters.Single(filter => filter.Descriptor.Id == DrawingLensProvider.IncludeOff));

        Assert.Equal(2, model.ObjectCount);
        Assert.Equal(reads, actions.ReadCount);
        Assert.Equal(requests, actions.RequestCount);
        Assert.Equal(["1", "3"], model.Groups.SelectMany(group => group.Objects).Select(id => id.DisplayId).Order());

        actions.NativeSelection = [new TestEntityId(2)];
        await model.ReadCommand.ExecuteAsync(null);
        Assert.Equal("2", Assert.Single(model.Groups.SelectMany(group => group.Objects)).DisplayId);
        Assert.Equal(requests + 1, actions.RequestCount);
        await model.ShowAllObjectsCommand.ExecuteAsync(null);
        Assert.True(model.IsAllObjects);
        Assert.Equal("Refresh active space", model.RefreshLabel);
        Assert.Equal(3, model.ObjectCount);
    }

    /// <summary>An empty selected scope never expands into the whole drawing.</summary>
    [Fact]
    public async Task EmptySelectionShowsAnExplicitEmptyState()
    {
        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.Layers);
        await model.ActivateAsync(CancellationToken.None);
        await model.ShowSelectedObjectsCommand.ExecuteAsync(null);

        Assert.Empty(model.Groups);
        Assert.Contains("Refresh to select objects", model.EmptyMessage);
        Assert.True(model.ReadCommand.CanExecute(null));
        Assert.False(model.SelectCommand.CanExecute(null));
    }

    /// <summary>A failed request or cleanup keeps the previous scope, navigation, and owned drawing effects.</summary>
    [Theory]
    [InlineData(false, "selection fixture failure")]
    [InlineData(true, "selection fixture failure")]
    [InlineData(false, "Object selection canceled. Previous view kept.")]
    [InlineData(true, "Object selection canceled. Previous view kept.")]
    [InlineData(false, null)]
    [InlineData(true, null)]
    public async Task FailedSelectionOrScopeSwitchKeepsPreviousTargets(bool startsSelected, string? requestFailure)
    {
        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.Layers);
        await model.ActivateAsync(CancellationToken.None);

        if (startsSelected)
        {
            actions.NativeSelection = [new TestEntityId(1)];
            await model.ShowSelectedObjectsCommand.ExecuteAsync(null);
        }

        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        await model.SelectCommand.ExecuteAsync(null);
        await model.IsolateCommand.ExecuteAsync(null);
        var current = model.Current;
        var groups = model.Groups;
        var selected = actions.NativeSelection;
        var isolated = actions.Isolated;
        var reads = actions.ReadCount;
        actions.RequestFailure = requestFailure;
        actions.CleanupFails = requestFailure is null;
        actions.Calls.Clear();
        var command = startsSelected
            ? requestFailure is null ? model.ShowAllObjectsCommand : model.ReadCommand
            : model.ShowSelectedObjectsCommand;
        await command.ExecuteAsync(null);

        Assert.Equal(startsSelected, model.IsSelectedObjectsOnly);
        Assert.Same(current, model.Current);
        Assert.Equal(groups, model.Groups);
        Assert.Equal(selected, actions.NativeSelection);
        Assert.Equal(isolated, actions.Isolated);
        Assert.Equal(reads, actions.ReadCount);
        Assert.Contains(requestFailure ?? "Cleanup did not complete", model.Status);

        if (requestFailure is not null)
            Assert.Equal(["request"], actions.Calls);
    }

    /// <summary>Collapsing, switching lenses, and reactivation keep the captured set despite native cleanup.</summary>
    [Fact]
    public async Task ReactivationKeepsCapturedSelectionAndNavigation()
    {
        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.ObjectTypes);
        using var other = new ObjectExplorerViewModel(actions, DrawingGrouping.Layers);
        await model.ActivateAsync(CancellationToken.None);
        actions.NativeSelection = [new TestEntityId(1)];
        await model.ShowSelectedObjectsCommand.ExecuteAsync(null);
        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        var current = model.Current!.Id;
        await model.DeactivateAsync(CancellationToken.None);
        Assert.Empty(actions.NativeSelection);
        await other.ActivateAsync(CancellationToken.None);
        await other.DeactivateAsync(CancellationToken.None);
        var reads = actions.ReadCount;
        var requests = actions.RequestCount;
        await model.ActivateAsync(CancellationToken.None);

        Assert.Equal(reads, actions.ReadCount);
        Assert.Equal(requests, actions.RequestCount);
        Assert.Equal(current, model.Current!.Id);
        Assert.Equal("1", Assert.Single(model.Current.Objects).DisplayId);
        Assert.True(model.IsSelectedObjectsOnly);
    }

    /// <summary>Excluding the current selected target clears both native selection and isolation without recapturing.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedFilterRemovalClearsCurrentEffects(bool cleanupFails)
    {
        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.Layers);
        await model.ActivateAsync(CancellationToken.None);
        actions.NativeSelection = [new TestEntityId(3)];
        await model.ShowSelectedObjectsCommand.ExecuteAsync(null);
        Assert.Contains("inclusion filters", model.EmptyMessage);
        await model.ToggleFilterCommand.ExecuteAsync(model.Filters.Single(filter => filter.Descriptor.Id == DrawingLensProvider.IncludeOff));
        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        await model.SelectCommand.ExecuteAsync(null);
        await model.IsolateCommand.ExecuteAsync(null);
        Assert.Single(actions.NativeSelection);
        Assert.Single(actions.Isolated);
        var reads = actions.ReadCount;
        var requests = actions.RequestCount;
        actions.CleanupFails = cleanupFails;
        await model.ToggleFilterCommand.ExecuteAsync(model.Filters.Single(filter => filter.Descriptor.Id == DrawingLensProvider.IncludeOff));

        if (cleanupFails)
        {
            Assert.NotNull(model.Current);
            Assert.Single(model.Groups);
            Assert.Single(actions.NativeSelection);
            Assert.Single(actions.Isolated);
            Assert.Contains("Cleanup did not complete", model.Status);
        }
        else
        {
            Assert.Null(model.Current);
            Assert.Empty(model.Groups);
            Assert.Empty(actions.NativeSelection);
            Assert.Empty(actions.Isolated);
        }

        Assert.Equal(reads, actions.ReadCount);
        Assert.Equal(requests, actions.RequestCount);
    }

    /// <summary>Both selection entry paths wait for CAD selection and load it without a second refresh.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingSelectionDisablesCommandsAndLoadsImmediately(bool refresh)
    {
        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.Layers);
        await model.ActivateAsync(CancellationToken.None);

        if (refresh)
        {
            actions.NativeSelection = [new TestEntityId(1)];
            await model.ShowSelectedObjectsCommand.ExecuteAsync(null);
        }

        var groups = model.Groups;
        actions.PendingSelection = new TaskCompletionSource<HostResult<ImmutableArray<IPlacedObjectId>>>();
        actions.Calls.Clear();
        var request = (refresh ? model.ReadCommand : model.ShowSelectedObjectsCommand).ExecuteAsync(null);

        Assert.False(request.IsCompleted);
        Assert.True(model.IsBusy);
        Assert.False(model.ReadCommand.CanExecute(null));
        Assert.False(model.ShowSelectedObjectsCommand.CanExecute(null));
        Assert.False(model.ShowAllObjectsCommand.CanExecute(null));
        Assert.False(model.SelectCommand.CanExecute(null));
        Assert.False(model.ResetCommand.CanExecute(null));
        Assert.Equal(groups, model.Groups);
        Assert.Equal(refresh, model.IsSelectedObjectsOnly);
        Assert.Equal(["request"], actions.Calls);

        actions.PendingSelection.SetResult(
            new HostResult<ImmutableArray<IPlacedObjectId>>.Success([new TestEntityId(2)]));
        await request;

        Assert.Equal(["request", "clear", "read"], actions.Calls);
        Assert.Equal("2", Assert.Single(model.Groups.SelectMany(group => group.Objects)).DisplayId);
        Assert.True(model.IsSelectedObjectsOnly);
        Assert.False(model.IsBusy);
        Assert.True(model.ReadCommand.CanExecute(null));
    }

    /// <summary>A context reset or close rejects a selection result returned after request cancellation.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContextResetOrCloseRejectsLateSelection(bool close)
    {
        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.Layers);
        await model.ActivateAsync(CancellationToken.None);
        actions.NativeSelection = [new TestEntityId(1)];
        await model.ShowSelectedObjectsCommand.ExecuteAsync(null);
        var groups = model.Groups;
        var reads = actions.ReadCount;
        actions.PendingSelection = new TaskCompletionSource<HostResult<ImmutableArray<IPlacedObjectId>>>();
        actions.Calls.Clear();
        var refresh = model.ReadCommand.ExecuteAsync(null);

        if (close)
            model.Close(false);
        else
            await model.ResetContextAsync(false);

        actions.PendingSelection.SetResult(
            new HostResult<ImmutableArray<IPlacedObjectId>>.Success([new TestEntityId(2)]));
        await refresh;

        Assert.True(actions.LastRequestToken.IsCancellationRequested);
        Assert.Equal(reads, actions.ReadCount);
        Assert.Equal(["request"], actions.Calls);
        Assert.False(model.ReadCommand.CanExecute(null));

        if (close)
            Assert.Equal(groups, model.Groups);
        else
        {
            Assert.Empty(model.Groups);
            Assert.True(model.IsAllObjects);
            Assert.Equal("No active drawing", model.SpaceLabel);
        }
    }

    /// <summary>Drawing or space changes discard selected scope and reload all objects without requesting selection.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContextChangeResetsScopeWithoutRequestingSelection(bool collapsed)
    {
        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.ObjectTypes);
        await model.ActivateAsync(CancellationToken.None);
        actions.NativeSelection = [new TestEntityId(1)];
        await model.ShowSelectedObjectsCommand.ExecuteAsync(null);

        if (collapsed)
            await model.DeactivateAsync(CancellationToken.None);

        var requests = actions.RequestCount;
        await model.ResetContextAsync();

        if (collapsed)
            await model.ActivateAsync(CancellationToken.None);

        Assert.True(model.IsAllObjects);
        Assert.Equal(requests, actions.RequestCount);
        Assert.Equal(2, model.ObjectCount);
        Assert.Null(model.Current);
    }

    /// <summary>A context reset cancels selected refresh and rejects even a non-cooperative late provider result.</summary>
    [Fact]
    public async Task ContextChangeRejectsLateSelectedRefresh()
    {
        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.Layers);
        await model.ActivateAsync(CancellationToken.None);
        actions.NativeSelection = [new TestEntityId(1)];
        await model.ShowSelectedObjectsCommand.ExecuteAsync(null);
        actions.Pending = new TaskCompletionSource<HostResult<LensPresentation>>();
        var refresh = model.ReadCommand.ExecuteAsync(null);

        Assert.False(model.ShowAllObjectsCommand.CanExecute(null));
        await model.ResetContextAsync(false);
        actions.Pending.SetResult(new HostResult<LensPresentation>.Success(
            DrawingLensProvider.Build(Actions.Inventory, DrawingGrouping.Layers, new HashSet<string>())));
        await refresh;

        Assert.Empty(model.Groups);
        Assert.Equal("No active drawing", model.SpaceLabel);
        Assert.True(actions.LastReadToken.IsCancellationRequested);
        Assert.False(model.ReadCommand.CanExecute(null));
        Assert.True(model.IsAllObjects);
    }

    private sealed class Actions : IObjectExplorerActions
    {
        internal static readonly DrawingInventory Inventory = new(
            "Model",
            [new LayerSnapshot(new LayerId("visible"), "Visible", false, false, false, false),
                new LayerSnapshot(new LayerId("off"), "Off", true, false, false, false)],
            [new EntitySnapshot(new TestEntityId(1), new LayerId("visible"), "AcDbLine"),
                new EntitySnapshot(new TestEntityId(2), new LayerId("visible"), "AcDbCircle"),
                new EntitySnapshot(new TestEntityId(3), new LayerId("off"), "AcDbLine")]);

        private readonly DrawingLensProvider _provider = new(new Source());
        internal ImmutableArray<IPlacedObjectId> NativeSelection { get; set; } = [];
        internal ImmutableArray<IPlacedObjectId> Isolated { get; private set; } = [];
        internal List<string> Calls { get; } = [];
        internal int ReadCount { get; private set; }
        internal int RequestCount { get; private set; }
        internal CancellationToken LastReadToken { get; private set; }
        internal CancellationToken LastRequestToken { get; private set; }
        internal TaskCompletionSource<HostResult<LensPresentation>>? Pending { get; set; }
        internal TaskCompletionSource<HostResult<ImmutableArray<IPlacedObjectId>>>? PendingSelection { get; set; }
        internal bool CleanupFails { get; set; }
        internal string? RequestFailure { get; set; }

        public Task<HostResult<ImmutableArray<IPlacedObjectId>>> RequestObjectsAsync(CancellationToken cancellationToken)
        {
            Calls.Add("request");
            RequestCount++;
            LastRequestToken = cancellationToken;
            return PendingSelection?.Task ?? Task.FromResult<HostResult<ImmutableArray<IPlacedObjectId>>>(
                RequestFailure is { } reason
                    ? new HostResult<ImmutableArray<IPlacedObjectId>>.Unavailable(reason)
                    : new HostResult<ImmutableArray<IPlacedObjectId>>.Success(NativeSelection));
        }

        public Task<HostResult<LensPresentation>> ReadAsync(
            DrawingGrouping grouping,
            IReadOnlySet<string> enabledFilters,
            ImmutableArray<IPlacedObjectId>? selectedObjects,
            CancellationToken cancellationToken)
        {
            Calls.Add("read");
            ReadCount++;
            LastReadToken = cancellationToken;
            return Pending?.Task ?? _provider.LoadAsync(grouping, enabledFilters, selectedObjects, cancellationToken);
        }

        public void ClearImmediately(bool hostTerminating)
        {
            NativeSelection = [];
            Isolated = [];
        }

        public Task<HostResult<bool>> ClearAsync(CancellationToken cancellationToken)
        {
            Calls.Add("clear");

            if (CleanupFails)
                return Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(false));

            ClearImmediately(false);
            return Success();
        }

        public Task<HostResult<bool>> SelectAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken)
        {
            NativeSelection = objects;
            return Success();
        }

        public Task<HostResult<bool>> ClearIsolationAsync(CancellationToken cancellationToken)
        {
            Isolated = [];
            return Success();
        }
        public Task<string> FocusAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken) =>
            Task.FromResult("View fitted.");
        public Task<string> IsolateObjectsAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken)
        {
            Isolated = objects;
            return Task.FromResult("Isolated.");
        }
        private static Task<HostResult<bool>> Success() => Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(true));

        private sealed class Source : IDrawingInventorySource
        {
            public Task<HostResult<DrawingInventory>> ReadAsync(
                ImmutableArray<IPlacedObjectId>? selectedObjects,
                CancellationToken cancellationToken) =>
                Task.FromResult<HostResult<DrawingInventory>>(new HostResult<DrawingInventory>.Success(
                    selectedObjects is { } selected
                        ? Inventory with {Entities = [.. Inventory.Entities.Where(entity => selected.Contains(entity.Id))]}
                        : Inventory));
        }

        private sealed record LayerId(string DisplayId) : ILayerId;
    }
}
