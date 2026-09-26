using CadLens.Lenses;
using System.Collections.Immutable;
using Common;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>Layers exploration behavior over a detached inventory fixture.</summary>
public sealed class ExplorerNavigationTests
{
    /// <summary>Navigation restores broader emphasis and never invokes Focus.</summary>
    [Fact]
    public async Task BrowseObjectsAndReturnThroughBreadcrumbs()
    {
        var actions = new Actions();
        using var model = new LayersViewModel(actions);
        await ToggleAsync(model);
        var group = model.Items[0];
        model.EnterCommand.Execute(group);
        var type = model.Items[0];
        model.EnterCommand.Execute(type);
        Assert.Equal(type.Objects, actions.EmphasisTargets);
        model.EnterCommand.Execute(model.Items[0]);
        Assert.Equal(new TestEntityId(1), Assert.Single(actions.EmphasisTargets));

        Assert.Equal("1 of 2", model.ObjectPosition);
        Assert.False(model.PreviousCommand.CanExecute(null));
        Assert.True(model.NextCommand.CanExecute(null));
        Assert.Equal("Category", model.Current!.Fields[0].Label);
        model.NextCommand.Execute(null);
        Assert.Equal(new TestEntityId(2), Assert.Single(actions.EmphasisTargets));
        Assert.Equal("2 of 2", model.ObjectPosition);
        Assert.False(model.NextCommand.CanExecute(null));
        model.PreviousCommand.Execute(null);
        Assert.Equal("1 of 2", model.ObjectPosition);
        model.BackCommand.Execute(null);
        Assert.Same(type, model.Current);
        Assert.Equal(type.Objects, actions.EmphasisTargets);
        model.BreadcrumbCommand.Execute(group);
        Assert.Same(group, model.Current);
        Assert.Equal(group.Objects, actions.EmphasisTargets);
        model.RootCommand.Execute(null);
        Assert.Null(model.Current);
        Assert.Empty(model.Breadcrumbs);
        Assert.False(model.BackCommand.CanExecute(null));
        Assert.Equal(0, actions.HostCalls);
        Assert.Empty(actions.EmphasisTargets);
    }

    /// <summary>Filters are independent, preserve valid paths, and remove excluded selections.</summary>
    [Fact]
    public async Task FiltersReconcileSelectionAndStartOffInNewSessions()
    {
        var actions = new Actions();
        using var model = new LayersViewModel(actions);
        await ToggleAsync(model);
        Assert.All(model.Filters, filter => Assert.False(filter.IsEnabled));
        await model.ToggleFilterCommand.ExecuteAsync(model.Filters[0]);
        Assert.Equal(new[] { "archived" }, actions.Enabled.Order());
        model.EnterCommand.Execute(model.Items.Single(node => node.Id == "hidden"));
        await model.ToggleFilterCommand.ExecuteAsync(model.Filters[1]);
        Assert.Equal("hidden", model.Current!.Id);
        Assert.Equal(2, actions.Enabled.Count);
        await model.ToggleFilterCommand.ExecuteAsync(model.Filters[0]);
        Assert.Null(model.Current);
        Assert.Equal(new[] { "extra" }, actions.Enabled.Order());
        Assert.Empty(actions.EmphasisTargets);
        Assert.False(model.Filters[0].IsEnabled);
        Assert.True(model.Filters[1].IsEnabled);
        using var reopened = new LayersViewModel(actions);
        await ToggleAsync(reopened);
        Assert.Empty(actions.Enabled);
        Assert.All(reopened.Filters, filter => Assert.False(filter.IsEnabled));
    }

    /// <summary>Refresh retains existing objects and falls back to the nearest valid parent.</summary>
    [Fact]
    public async Task RefreshReconcilesObjectsAndSingletonEndpoints()
    {
        var actions = new Actions();
        using var model = new LayersViewModel(actions);
        await ToggleAsync(model);
        model.EnterCommand.Execute(model.Items[0]);
        model.EnterCommand.Execute(model.Items[0]);
        model.EnterCommand.Execute(model.Items[1]);
        await model.ReadCommand.ExecuteAsync(null);
        Assert.Equal("2 of 2", model.ObjectPosition);
        actions.SingleObject = true;
        await model.ReadCommand.ExecuteAsync(null);
        Assert.Equal("type", model.Current!.Id);
        Assert.Equal(model.Current.Objects, actions.EmphasisTargets);
        model.EnterCommand.Execute(model.Items[0]);
        Assert.Equal("1 of 1", model.ObjectPosition);
        Assert.False(model.PreviousCommand.CanExecute(null));
        Assert.False(model.NextCommand.CanExecute(null));
    }

    /// <summary>A failed graphics clear keeps the existing inventory and reports why refresh stopped.</summary>
    [Fact]
    public async Task RefreshStopsWhenOldEffectsCannotBeCleared()
    {
        var actions = new Actions();
        using var model = new LayersViewModel(actions);
        await ToggleAsync(model);
        var groups = model.Groups;
        var reads = actions.ReadCount;
        actions.ClearResult = new HostResult<bool>.Unavailable("fixture clear failed");

        await model.ReadCommand.ExecuteAsync(null);

        Assert.Equal(reads, actions.ReadCount);
        Assert.Equal(groups, model.Groups);
        Assert.Contains("fixture clear failed", model.Status);
    }

    /// <summary>A filter result cannot republish an old document's selection.</summary>
    [Fact]
    public async Task ContextChangeRejectsPendingFilterResult()
    {
        var actions = new Actions();
        using var model = new LayersViewModel(actions);
        await ToggleAsync(model);
        model.EnterCommand.Execute(model.Items[0]);
        actions.Pending = new TaskCompletionSource<HostResult<LayersPresentation>>();
        var pending = model.ToggleFilterCommand.ExecuteAsync(model.Filters[0]);
        Assert.False(model.EnterCommand.CanExecute(model.Items[0]));
        Assert.False(model.BackCommand.CanExecute(null));
        Assert.False(model.ToggleFilterCommand.CanExecute(model.Filters[1]));
        var reset = model.ResetContextAsync();
        var oldRead = actions.Pending;
        actions.Pending = null;
        oldRead.SetResult(new HostResult<LayersPresentation>.Success(CreatePresentation(false, true) with { SpaceLabel = "Old drawing" }));
        await Task.WhenAll(pending, reset);
        Assert.NotEmpty(model.Items);
        Assert.Empty(model.Breadcrumbs);
        Assert.Null(model.Current);
        Assert.Equal("Test space", model.SpaceLabel);
    }

    /// <summary>Empty results retain filter controls and the provider's explanation.</summary>
    [Fact]
    public async Task EmptyResultKeepsFiltersAndExplanation()
    {
        var actions = new Actions { Empty = true };
        using var model = new LayersViewModel(actions);
        await ToggleAsync(model);
        Assert.True(model.IsEmpty);
        Assert.Equal("Nothing matches these options.", model.EmptyMessage);
        Assert.Equal(2, model.Filters.Length);
    }

    /// <summary>A failed filter reload must not leave navigable results for the old options.</summary>
    [Fact]
    public async Task FailedFilterReadClearsStaleSelection()
    {
        var actions = new Actions();
        using var model = new LayersViewModel(actions);
        await ToggleAsync(model);
        model.EnterCommand.Execute(model.Items[0]);
        actions.Pending = new TaskCompletionSource<HostResult<LayersPresentation>>();
        var pending = model.ToggleFilterCommand.ExecuteAsync(model.Filters[0]);
        actions.Pending.SetException(new InvalidOperationException("read failed"));
        await pending;
        Assert.Empty(model.Items);
        Assert.Null(model.Current);
        Assert.True(model.Filters[0].IsEnabled);
        Assert.Contains("read failed", model.Status);
        Assert.Empty(actions.EmphasisTargets);
        Assert.True(model.ReadCommand.CanExecute(null));
    }

    /// <summary>Only explicit Focus sends the selected group's or object's identifiers to the host.</summary>
    [Fact]
    public async Task FocusIsExplicitAndUsesCurrentTargets()
    {
        var actions = new Actions();
        using var model = new LayersViewModel(actions);
        Assert.False(model.FocusCommand.CanExecute(null));
        await ToggleAsync(model);
        Assert.False(model.FocusCommand.CanExecute(null));
        model.EnterCommand.Execute(model.Items[0]);
        Assert.True(model.FocusCommand.CanExecute(null));
        Assert.Equal(0, actions.HostCalls);
        await model.FocusCommand.ExecuteAsync(null);
        Assert.Equal(model.Current!.Objects, actions.FocusTargets);
        Assert.Equal(2, actions.FocusTargets.Length);
        model.EnterCommand.Execute(model.Items[0]);
        model.EnterCommand.Execute(model.Items[0]);
        model.NextCommand.Execute(null);
        Assert.Equal(1, actions.HostCalls);
        await model.FocusCommand.ExecuteAsync(null);
        Assert.Equal(new TestEntityId(2), Assert.Single(actions.FocusTargets));
        Assert.Equal("2 of 2", model.ObjectPosition);
        model.RootCommand.Execute(null);
        Assert.False(model.FocusCommand.CanExecute(null));
    }

    /// <summary>Auto Focus follows the selected node and stops when switched off.</summary>
    [Fact]
    public async Task AutoFocusFollowsNavigationUntilTurnedOff()
    {
        var actions = new Actions();
        using var model = new LayersViewModel(actions);
        await ToggleAsync(model);
        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        Assert.Equal(0, actions.FocusCount);

        await model.ToggleAutoFocusCommand.ExecuteAsync(null);
        Assert.Equal(model.Current!.Objects, actions.FocusTargets);

        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        await model.EnterCommand.ExecuteAsync(model.Items[1]);
        Assert.Equal(new TestEntityId(2), Assert.Single(actions.FocusTargets));
        Assert.Equal(3, actions.FocusCount);

        await model.ToggleAutoFocusCommand.ExecuteAsync(null);
        await model.PreviousCommand.ExecuteAsync(null);
        Assert.Equal(3, actions.FocusCount);
    }

    /// <summary>Manual highlighting works while automatic navigation highlighting is off.</summary>
    [Fact]
    public async Task HighlightCanBeManualOrAutomatic()
    {
        var actions = new Actions();
        using var model = new LayersViewModel(actions);
        await ToggleAsync(model);
        Assert.True(model.IsAutoHighlight);

        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        Assert.Equal(model.Current!.Objects, actions.EmphasisTargets);
        await model.ToggleAutoHighlightCommand.ExecuteAsync(null);
        Assert.False(model.IsAutoHighlight);
        Assert.Empty(actions.EmphasisTargets);

        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        Assert.Empty(actions.EmphasisTargets);
        await model.HighlightCommand.ExecuteAsync(null);
        Assert.Equal(model.Current!.Objects, actions.EmphasisTargets);
        await model.BackCommand.ExecuteAsync(null);
        Assert.Empty(actions.EmphasisTargets);

        await model.ToggleAutoHighlightCommand.ExecuteAsync(null);
        Assert.True(model.IsAutoHighlight);
        Assert.Equal(model.Current!.Objects, actions.EmphasisTargets);
    }

    /// <summary>Unavailable bounds are explained without losing navigation or leaving commands busy.</summary>
    [Fact]
    public async Task FocusUnavailableKeepsSelection()
    {
        var actions = new Actions { FocusMessage = "No usable bounds." };
        using var model = new LayersViewModel(actions);
        await ToggleAsync(model);
        model.EnterCommand.Execute(model.Items[0]);
        var selected = model.Current;
        await model.FocusCommand.ExecuteAsync(null);
        Assert.Same(selected, model.Current);
        Assert.Equal("No usable bounds.", model.Status);
        Assert.True(model.FocusCommand.CanExecute(null));
    }

    /// <summary>A context switch cancels the queued focus and rejects its late status.</summary>
    [Fact]
    public async Task ContextChangeCancelsPendingFocus()
    {
        var actions = new Actions { PendingFocus = new TaskCompletionSource<string>() };
        using var model = new LayersViewModel(actions);
        await ToggleAsync(model);
        model.EnterCommand.Execute(model.Items[0]);
        var pending = model.FocusCommand.ExecuteAsync(null);
        Assert.False(model.NextCommand.CanExecute(null));
        Assert.False(model.FocusCommand.CanExecute(null));
        var reset = model.ResetContextAsync();
        Assert.True(actions.FocusToken.IsCancellationRequested);
        actions.PendingFocus.SetResult("Old focus finished.");
        await Task.WhenAll(pending, reset);
        Assert.DoesNotContain("Old focus", model.Status);
        Assert.False(model.FocusCommand.CanExecute(null));
    }

    /// <summary>Generic providers can omit Focus even when their nodes contain objects.</summary>
    [Fact]
    public async Task FocusRequiresProviderAction()
    {
        var actions = new Actions { AllowFocus = false };
        using var model = new LayersViewModel(actions);
        await ToggleAsync(model);
        model.EnterCommand.Execute(model.Items[0]);
        Assert.False(model.FocusCommand.CanExecute(null));
    }

    /// <summary>Pending emphasis serializes navigation and context changes cancel its native request.</summary>
    [Fact]
    public async Task ContextChangeCancelsNavigationHighlight()
    {
        var actions = new Actions();
        using var model = new LayersViewModel(actions);
        await ToggleAsync(model);
        actions.PendingEmphasis = new TaskCompletionSource<string>();
        var pending = model.EnterCommand.ExecuteAsync(model.Items[0]);
        Assert.True(model.IsBusy);
        Assert.False(model.BackCommand.CanExecute(null));
        Assert.False(model.FocusCommand.CanExecute(null));
        var reset = model.ResetContextAsync();
        Assert.True(actions.EmphasisToken.IsCancellationRequested);
        actions.PendingEmphasis.SetResult("Old highlight completed.");
        await Task.WhenAll(pending, reset);
        Assert.Null(model.Current);
        Assert.DoesNotContain("Old highlight", model.Status);
        Assert.False(model.IsBusy);
    }

    /// <summary>A graphics failure is reported without breaking navigation or implicitly focusing.</summary>
    [Fact]
    public async Task FailedHighlightKeepsNavigationUsable()
    {
        var actions = new Actions();
        using var model = new LayersViewModel(actions);
        await ToggleAsync(model);
        actions.PendingEmphasis = new TaskCompletionSource<string>();
        var pending = model.EnterCommand.ExecuteAsync(model.Items[0]);
        actions.PendingEmphasis.SetException(new InvalidOperationException("Graphics failed."));
        await pending;
        Assert.Contains("Graphics failed", model.Status);
        Assert.NotNull(model.Current);
        Assert.True(model.BackCommand.CanExecute(null));
        actions.PendingEmphasis = null;
        await model.RootCommand.ExecuteAsync(null);
        Assert.Empty(actions.EmphasisTargets);
        Assert.Equal(0, actions.HostCalls);
    }

    /// <summary>Closing the panel cancels outstanding emphasis and ignores its eventual result.</summary>
    [Fact]
    public async Task ClosingCancelsNavigationHighlight()
    {
        var actions = new Actions();
        var model = new LayersViewModel(actions);
        await ToggleAsync(model);
        actions.PendingEmphasis = new TaskCompletionSource<string>();
        var pending = model.EnterCommand.ExecuteAsync(model.Items[0]);
        model.Dispose();
        Assert.True(actions.EmphasisToken.IsCancellationRequested);
        actions.PendingEmphasis.SetResult("Late highlight.");
        await pending;
        Assert.DoesNotContain("Late highlight", model.Status);
        Assert.False(model.RootCommand.CanExecute(null));
    }

    /// <summary>No-drawing state clears selection, disables host commands, and recovers on activation.</summary>
    [Fact]
    public async Task ClosingLastDrawingDisablesActionsUntilActivation()
    {
        var actions = new Actions();
        using var model = new LayersViewModel(actions);
        await ToggleAsync(model);
        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        await model.ResetContextAsync(false);
        Assert.Empty(model.Groups);
        Assert.Null(model.Current);
        Assert.Equal("No active drawing", model.SpaceLabel);
        Assert.False(model.ReadCommand.CanExecute(null));
        Assert.False(model.HighlightCommand.CanExecute(null));
        Assert.False(model.ResetCommand.CanExecute(null));
        Assert.False(model.FocusCommand.CanExecute(null));
        Assert.False(model.ToggleFilterCommand.CanExecute(model.Filters[0]));
        await model.ResetContextAsync();
        Assert.True(model.ReadCommand.CanExecute(null));
        Assert.NotEmpty(model.Groups);
        Assert.Null(model.Current);
    }

    /// <summary>Switching drawings retains session filter options but starts at the new root.</summary>
    [Fact]
    public async Task DrawingSwitchKeepsFiltersAndResetsNavigation()
    {
        var actions = new Actions();
        using var model = new LayersViewModel(actions);
        await ToggleAsync(model);
        await model.ToggleFilterCommand.ExecuteAsync(model.Filters[0]);
        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        await model.ResetContextAsync(false);
        await model.ResetContextAsync();
        Assert.Contains("archived", actions.Enabled);
        Assert.Null(model.Current);
        Assert.Empty(model.Breadcrumbs);
        Assert.Empty(actions.EmphasisTargets);
    }

    /// <summary>Inactive commands cannot read or emphasize; activation loads only the root.</summary>
    [Fact]
    public async Task NewSessionDoesNoDrawingWorkUntilActivation()
    {
        var actions = new Actions();
        using var model = new LayersViewModel(actions);
        Assert.False(model.IsLensActive);
        Assert.False(model.ReadCommand.CanExecute(null));
        await model.ReadCommand.ExecuteAsync(null);
        await model.HighlightCommand.ExecuteAsync(null);
        Assert.Equal(0, actions.ReadCount);
        Assert.Equal(0, actions.HostCalls);
        await ToggleAsync(model);
        Assert.True(model.IsLensActive);
        Assert.Equal(1, actions.ReadCount);
        Assert.Null(model.Current);
        Assert.Empty(actions.EmphasisTargets);
        Assert.Equal(0, actions.HostCalls);
    }

    /// <summary>Collapse preserves filters and valid selections, while erasure reconciles to a parent.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReactivationRestoresValidNavigation(bool eraseSelected)
    {
        var actions = new Actions();
        using var model = new LayersViewModel(actions);
        await ToggleAsync(model);
        await model.ToggleFilterCommand.ExecuteAsync(model.Filters[0]);
        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        await model.EnterCommand.ExecuteAsync(model.Items[1]);
        await ToggleAsync(model);
        Assert.False(model.IsLensActive);
        Assert.Empty(actions.EmphasisTargets);
        Assert.Equal("second", model.Current!.Id);
        actions.SingleObject = eraseSelected;
        await ToggleAsync(model);
        Assert.Equal(eraseSelected ? "type" : "second", model.Current!.Id);
        Assert.Equal(model.Current.Objects, actions.EmphasisTargets);
        Assert.True(model.Filters[0].IsEnabled);
        Assert.Equal(0, actions.FocusCount);
    }

    /// <summary>Root restoration and compact context changes never restore an old selection.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompactContextAndRootRemainUnselected(bool changeContext)
    {
        var actions = new Actions();
        using var model = new LayersViewModel(actions);
        await ToggleAsync(model);

        if (changeContext)
            await model.EnterCommand.ExecuteAsync(model.Items[0]);

        await ToggleAsync(model);
        var reads = actions.ReadCount;

        if (changeContext)
        {
            await model.ResetContextAsync(false);
            await model.ResetContextAsync();
        }

        Assert.False(model.IsLensActive);
        await model.ReadCommand.ExecuteAsync(null);
        Assert.Equal(reads, actions.ReadCount);
        await ToggleAsync(model);
        Assert.Equal(reads + 1, actions.ReadCount);
        Assert.Null(model.Current);
        Assert.Empty(actions.EmphasisTargets);
    }

    /// <summary>Late emphasis settles before independent cleanup and cannot replace its status.</summary>
    [Fact]
    public async Task CollapseWaitsForLateEmphasisAndCleanup()
    {
        var actions = new Actions();
        using var model = new LayersViewModel(actions);
        await ToggleAsync(model);
        actions.PendingEmphasis = new TaskCompletionSource<string>();
        actions.PendingClear = new TaskCompletionSource<HostResult<bool>>();
        actions.ClearStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var emphasis = model.EnterCommand.ExecuteAsync(model.Items[0]);
        var collapse = ToggleAsync(model);
        Assert.False(model.IsLensActive);
        Assert.True(model.IsBusy);
        Assert.True(actions.EmphasisToken.IsCancellationRequested);
        Assert.Equal(1, actions.ClearCount);
        actions.PendingEmphasis.SetResult("Old emphasis completed.");
        await emphasis;
        await actions.ClearStarted.Task;
        Assert.False(actions.ClearToken.IsCancellationRequested);
        Assert.True(model.IsBusy);
        Assert.DoesNotContain("Old emphasis", model.Status);
        actions.PendingClear.SetResult(new HostResult<bool>.Success(true));
        await collapse;
        Assert.False(model.IsBusy);
        Assert.Empty(actions.EmphasisTargets);
    }

    /// <summary>Late activation inventory cannot publish content after collapse.</summary>
    [Fact]
    public async Task CollapseRejectsLateActivationInventory()
    {
        var actions = new Actions { Pending = new TaskCompletionSource<HostResult<LayersPresentation>>() };
        using var model = new LayersViewModel(actions);
        var activation = ToggleAsync(model);
        var collapse = ToggleAsync(model);
        actions.Pending.SetResult(new HostResult<LayersPresentation>.Success(CreatePresentation(false, false)));
        await Task.WhenAll(activation, collapse);
        Assert.False(model.IsLensActive);
        Assert.Empty(model.Groups);
        Assert.Empty(actions.EmphasisTargets);
        Assert.Equal("Selection and highlight cleared. Auto settings kept.", model.Status);
    }

    /// <summary>Cleanup failures stay visible in compact mode without claiming effects were removed.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupFailureIsReported(bool throws)
    {
        var actions = new Actions();
        using var model = new LayersViewModel(actions);
        await ToggleAsync(model);
        actions.PendingClear = new TaskCompletionSource<HostResult<bool>>();
        var collapse = ToggleAsync(model);

        if (throws)
            actions.PendingClear.SetException(new InvalidOperationException("fixture cleanup failure"));
        else
            actions.PendingClear.SetResult(new HostResult<bool>.Unavailable("fixture cleanup unavailable"));

        await collapse;
        Assert.False(model.IsLensActive);
        Assert.False(model.IsBusy);
        Assert.Contains("fixture cleanup", model.Status);
        Assert.DoesNotContain("effects cleared", model.Status);
    }

    /// <summary>Closing cancels pending cleanup and ignores its late success.</summary>
    [Fact]
    public async Task CloseDuringCleanupRejectsLateCompletion()
    {
        var actions = new Actions();
        var model = new LayersViewModel(actions);
        await ToggleAsync(model);
        actions.PendingClear = new TaskCompletionSource<HostResult<bool>>();
        var collapse = ToggleAsync(model);
        model.Dispose();
        actions.PendingClear.SetResult(new HostResult<bool>.Success(true));
        await collapse;
        Assert.DoesNotContain("effects cleared", model.Status);
    }

    /// <summary>Repeated context changes replace old work with one fresh root read unless collapsed.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContextChangesReloadOnceAfterPendingWork(bool collapse)
    {
        var actions = new Actions();
        using var model = new LayersViewModel(actions);
        await model.ActivateAsync(CancellationToken.None);
        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        var oldRead = new TaskCompletionSource<HostResult<LayersPresentation>>();
        actions.Pending = oldRead;
        var read = model.ReadCommand.ExecuteAsync(null);
        var firstReset = model.ResetContextAsync();
        var secondReset = model.ResetContextAsync();
        var cleanup = collapse ? model.DeactivateAsync(CancellationToken.None) : Task.CompletedTask;

        actions.Pending = null;
        oldRead.SetResult(new HostResult<LayersPresentation>.Success(CreatePresentation(false, false) with { SpaceLabel = "Old drawing" }));
        await Task.WhenAll(read, firstReset, secondReset, cleanup);

        Assert.Equal(collapse ? 2 : 3, actions.ReadCount);
        Assert.False(model.IsBusy);
        Assert.Null(model.Current);
        Assert.Empty(actions.EmphasisTargets);
        Assert.NotEqual("Old drawing", model.SpaceLabel);
        Assert.Equal(!collapse, model.ReadCommand.CanExecute(null));
    }

    /// <summary>Automatic selection covers every navigation path without camera movement.</summary>
    [Fact]
    public async Task AutoSelectionFollowsEveryLevelWithoutMovingCamera()
    {
        var actions = new Actions();
        using var model = new LayersViewModel(actions);
        await model.ActivateAsync(CancellationToken.None);
        Assert.False(model.IsAutoFocus);
        Assert.False(model.IsAutoSelect);
        Assert.True(model.IsAutoHighlight);
        Assert.False(model.SelectCommand.CanExecute(null));
        await model.ToggleAutoHighlightCommand.ExecuteAsync(null);
        await model.ToggleAutoSelectCommand.ExecuteAsync(null);
        Assert.Empty(actions.SelectionTargets);
        var group = model.Items[0];

        await model.EnterCommand.ExecuteAsync(group);
        Assert.Equal(group.Objects, actions.SelectionTargets);
        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        Assert.Equal(model.Current!.Objects, actions.SelectionTargets);
        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        Assert.Equal(new TestEntityId(1), Assert.Single(actions.SelectionTargets));
        await model.NextCommand.ExecuteAsync(null);
        Assert.Equal(new TestEntityId(2), Assert.Single(actions.SelectionTargets));
        await model.PreviousCommand.ExecuteAsync(null);
        Assert.Equal(new TestEntityId(1), Assert.Single(actions.SelectionTargets));
        await model.BackCommand.ExecuteAsync(null);
        Assert.Equal(model.Current!.Objects, actions.SelectionTargets);
        await model.BreadcrumbCommand.ExecuteAsync(group);
        Assert.Equal(group.Objects, actions.SelectionTargets);
        await model.RootCommand.ExecuteAsync(null);
        Assert.Empty(actions.SelectionTargets);
        Assert.Empty(actions.EmphasisTargets);
        Assert.Equal(0, actions.FocusCount);
        Assert.True(model.IsAutoSelect);
    }

    /// <summary>Reset turns off Auto modes and clears effects while preserving navigation and camera.</summary>
    [Fact]
    public async Task ResetTurnsOffAutoModesAndKeepsEffectsClearedOnNavigation()
    {
        var actions = new Actions();
        using var model = new LayersViewModel(actions);
        await model.ActivateAsync(CancellationToken.None);
        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        await model.ToggleAutoSelectCommand.ExecuteAsync(null);
        await model.ToggleAutoFocusCommand.ExecuteAsync(null);
        Assert.Equal(model.Current!.Objects, actions.SelectionTargets);
        var current = model.Current;
        var camera = actions.FocusTargets;
        var focuses = actions.FocusCount;
        var filters = model.Filters;

        await model.ResetCommand.ExecuteAsync(null);

        Assert.Same(current, model.Current);
        Assert.Equal(filters, model.Filters);
        Assert.Equal(camera, actions.FocusTargets);
        Assert.Equal(focuses, actions.FocusCount);
        Assert.Empty(actions.SelectionTargets);
        Assert.Empty(actions.EmphasisTargets);
        Assert.False(model.IsAutoFocus || model.IsAutoSelect || model.IsAutoHighlight);
        Assert.Equal("Selection and highlight cleared. Auto modes off.", model.Status);
        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        Assert.Empty(actions.SelectionTargets);
        Assert.Empty(actions.EmphasisTargets);
        Assert.Equal(focuses, actions.FocusCount);
    }

    /// <summary>Manual actions preserve states previously set by the other actions.</summary>
    [Fact]
    public async Task ManualActionsDoNotReplaceEachOthersTargets()
    {
        var actions = new Actions();
        using var model = new LayersViewModel(actions);
        await model.ActivateAsync(CancellationToken.None);
        await model.ToggleAutoHighlightCommand.ExecuteAsync(null);
        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        await model.SelectCommand.ExecuteAsync(null);
        var selected = actions.SelectionTargets;
        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        await model.HighlightCommand.ExecuteAsync(null);
        Assert.Equal(selected, actions.SelectionTargets);
        Assert.Equal(0, actions.FocusCount);
        var highlighted = actions.EmphasisTargets;
        await model.FocusCommand.ExecuteAsync(null);
        Assert.Equal(selected, actions.SelectionTargets);
        Assert.Equal(highlighted, actions.EmphasisTargets);
        var camera = actions.FocusTargets;
        await model.NextCommand.ExecuteAsync(null);
        Assert.Empty(actions.EmphasisTargets);
        await model.SelectCommand.ExecuteAsync(null);
        Assert.Equal(new TestEntityId(2), Assert.Single(actions.SelectionTargets));
        Assert.Equal(camera, actions.FocusTargets);
        Assert.Empty(actions.EmphasisTargets);
        Assert.False(model.IsAutoFocus || model.IsAutoSelect || model.IsAutoHighlight);
    }

    /// <summary>Disabling one automatic effect preserves the other.</summary>
    [Fact]
    public async Task AutoToggleCleanupIsIndependent()
    {
        var actions = new Actions();
        using var model = new LayersViewModel(actions);
        await model.ActivateAsync(CancellationToken.None);
        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        await model.ToggleAutoSelectCommand.ExecuteAsync(null);
        await model.ToggleAutoHighlightCommand.ExecuteAsync(null);
        Assert.Equal(model.Current!.Objects, actions.SelectionTargets);
        Assert.Empty(actions.EmphasisTargets);
        await model.ToggleAutoHighlightCommand.ExecuteAsync(null);
        Assert.Equal(model.Current.Objects, actions.EmphasisTargets);
        await model.ToggleAutoSelectCommand.ExecuteAsync(null);
        Assert.Empty(actions.SelectionTargets);
        Assert.Equal(model.Current.Objects, actions.EmphasisTargets);
        Assert.Equal(0, actions.FocusCount);
    }

    /// <summary>Selection and highlight run even when Focus cannot fit the target.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnavailableFocusDoesNotBlockOtherAutoModes(bool allowFocus)
    {
        var actions = new Actions { AllowFocus = allowFocus, FocusMessage = "Focus unavailable: locked viewport." };
        using var model = new LayersViewModel(actions);
        await model.ActivateAsync(CancellationToken.None);
        await model.ToggleAutoSelectCommand.ExecuteAsync(null);
        await model.ToggleAutoFocusCommand.ExecuteAsync(null);
        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        Assert.Equal(model.Current!.Objects, actions.SelectionTargets);
        Assert.Equal(model.Current.Objects, actions.EmphasisTargets);
        Assert.StartsWith("Focus unavailable", model.Status);
        Assert.True(model.SelectCommand.CanExecute(null));
        Assert.Equal(allowFocus, model.FocusCommand.CanExecute(null));
    }

    /// <summary>Restoration reapplies selection and highlight but never refits the camera.</summary>
    [Fact]
    public async Task ReopeningRestoresSelectionWithoutAutoFocus()
    {
        var actions = new Actions();
        using var model = new LayersViewModel(actions);
        await model.ActivateAsync(CancellationToken.None);
        await model.ToggleAutoSelectCommand.ExecuteAsync(null);
        await model.ToggleAutoFocusCommand.ExecuteAsync(null);
        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        var camera = actions.FocusTargets;
        var focuses = actions.FocusCount;
        await model.DeactivateAsync(CancellationToken.None);
        Assert.Empty(actions.SelectionTargets);
        Assert.Empty(actions.EmphasisTargets);
        await model.ActivateAsync(CancellationToken.None);
        Assert.Equal(model.Current!.Objects, actions.SelectionTargets);
        Assert.Equal(model.Current.Objects, actions.EmphasisTargets);
        Assert.Equal(camera, actions.FocusTargets);
        Assert.Equal(focuses, actions.FocusCount);
        Assert.True(model.IsAutoFocus && model.IsAutoSelect && model.IsAutoHighlight);
    }

    /// <summary>Cancellation prevents pending selection from triggering further drawing actions.</summary>
    [Theory]
    [InlineData("collapse")]
    [InlineData("close")]
    [InlineData("context")]
    public async Task PendingSelectionCannotContinueAfterCleanup(string boundary)
    {
        var actions = new Actions();
        using var model = new LayersViewModel(actions);
        await model.ActivateAsync(CancellationToken.None);
        await model.ToggleAutoSelectCommand.ExecuteAsync(null);
        await model.ToggleAutoFocusCommand.ExecuteAsync(null);
        var completion = new TaskCompletionSource<HostResult<bool>>();
        actions.PendingSelection = completion;
        var navigation = model.EnterCommand.ExecuteAsync(model.Items[0]);
        Assert.False(model.ResetCommand.CanExecute(null));
        Assert.False(model.ToggleAutoSelectCommand.CanExecute(null));

        Task cleanup = Task.CompletedTask;
        switch (boundary)
        {
            case "collapse": cleanup = model.DeactivateAsync(CancellationToken.None); break;
            case "context": cleanup = model.ResetContextAsync(false); break;
            case "close": model.Close(false); break;
        }

        Assert.True(actions.SelectionToken.IsCancellationRequested);
        actions.PendingSelection = null;
        completion.SetResult(new HostResult<bool>.Success(true));
        await Task.WhenAll(navigation, cleanup);
        Assert.Empty(actions.SelectionTargets);
        Assert.Empty(actions.EmphasisTargets);
        Assert.Equal(0, actions.FocusCount);
    }

    /// <summary>A settled inactive lens does not clear preselection when opened, closed, or notified of context changes.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InactiveLensPreservesExternalSelection(bool previouslyActive)
    {
        var actions = new Actions();
        using var model = new LayersViewModel(actions);

        if (previouslyActive)
        {
            await model.ActivateAsync(CancellationToken.None);
            await model.EnterCommand.ExecuteAsync(model.Items[0]);
            await model.DeactivateAsync(CancellationToken.None);
        }

        await actions.SelectAsync([new TestEntityId(99)], CancellationToken.None);
        await model.ResetContextAsync(false);
        await model.ResetContextAsync(true);
        model.Close(false);
        Assert.Equal(new TestEntityId(99), Assert.Single(actions.SelectionTargets));
    }

    /// <summary>Failed collapse keeps cleanup owed even after the lens becomes inactive and idle.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedCollapseStillCleansOnCloseOrContextChange(bool contextChange)
    {
        var actions = new Actions();
        using var model = new LayersViewModel(actions);
        await model.ActivateAsync(CancellationToken.None);
        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        await model.SelectCommand.ExecuteAsync(null);
        actions.ClearResult = new HostResult<bool>.Unavailable("Cleanup failed.");
        await model.DeactivateAsync(CancellationToken.None);
        Assert.False(model.IsLensActive);
        Assert.False(model.IsBusy);
        Assert.NotEmpty(actions.SelectionTargets);

        if (contextChange)
            await model.ResetContextAsync(false);
        else
            model.Close(false);

        Assert.Empty(actions.SelectionTargets);
        Assert.Empty(actions.EmphasisTargets);
    }

    private static Task ToggleAsync(LayersViewModel model) => model.IsLensActive
        ? model.DeactivateAsync(CancellationToken.None)
        : model.ActivateAsync(CancellationToken.None);

    private static LayersPresentation CreatePresentation(bool single, bool hidden)
    {
        var first = new LensNode("first", "First", [new TestEntityId(1)], [], [new DetailField("Category", "Fixture")], [LensAction.Focus]);
        var second = new LensNode("second", "Second", [new TestEntityId(2)], [], first.Fields, [LensAction.Focus]);
        ImmutableArray<LensNode> objects = single ? [first] : [first, second];
        var type = new LensNode("type", "Fixture type", [.. objects.SelectMany(node => node.Objects)], objects, [], [LensAction.Focus]);
        var group = new LensNode("group", "Fixture group", type.Objects, [type], [], [LensAction.Focus]);
        ImmutableArray<LensNode> groups = hidden ? [group, group with { Id = "hidden", Label = "Archived group" }] : [group];

        return new LayersPresentation(
            "Fixture",
            "Test space",
            groups,
            [new BooleanFilter("archived", "Archived", "Include archived items", IconRole.Snowflake),
             new BooleanFilter("extra", "Extra", "Include extra items", IconRole.Lightbulb)],
            "Nothing matches these options.");
    }

    private sealed class Actions : ILayersActions
    {
        public void ClearImmediately(bool hostTerminating)
        {
            SelectionTargets = [];
            EmphasisTargets = [];
        }

        internal ImmutableArray<IPlacedObjectId> SelectionTargets { get; private set; } = [];
        internal TaskCompletionSource<HostResult<bool>>? PendingSelection { get; set; }
        internal CancellationToken SelectionToken { get; private set; }

        public Task<HostResult<bool>> SelectAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken)
        {
            SelectionToken = cancellationToken;
            SelectionTargets = objects;
            return PendingSelection?.Task ?? Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(true));
        }

        public Task<HostResult<bool>> ClearHighlightAsync(CancellationToken cancellationToken)
        {
            EmphasisTargets = [];
            return Task.FromResult(ClearResult);
        }

        internal IReadOnlySet<string> Enabled { get; private set; } = new HashSet<string>();
        internal int ReadCount { get; private set; }
        internal int ClearCount { get; private set; }
        internal int FocusCount { get; private set; }
        internal CancellationToken ClearToken { get; private set; }
        internal TaskCompletionSource<HostResult<bool>>? PendingClear { get; set; }
        internal HostResult<bool> ClearResult { get; set; } = new HostResult<bool>.Success(true);
        internal TaskCompletionSource ClearStarted { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool SingleObject { get; set; }
        internal bool Empty { get; init; }
        internal int HostCalls { get; private set; }
        internal ImmutableArray<IPlacedObjectId> EmphasisTargets { get; private set; } = [];
        internal TaskCompletionSource<string>? PendingEmphasis { get; set; }
        internal CancellationToken EmphasisToken { get; private set; }
        internal bool AllowFocus { get; init; } = true;
        internal string FocusMessage { get; init; } = "Focused.";
        internal ImmutableArray<IPlacedObjectId> FocusTargets { get; private set; }
        internal CancellationToken FocusToken { get; private set; }
        internal TaskCompletionSource<string>? PendingFocus { get; init; }
        internal TaskCompletionSource<HostResult<LayersPresentation>>? Pending { get; set; }

        public Task<HostResult<LayersPresentation>> ReadAsync(IReadOnlySet<string> enabledFilters, CancellationToken cancellationToken)
        {
            ReadCount++;
            Enabled = enabledFilters;
            var presentation = CreatePresentation(SingleObject, Enabled.Contains("archived"));

            if (Empty)
                presentation = presentation with { Groups = [] };

            if (!AllowFocus)
                presentation = presentation with { Groups = [.. presentation.Groups.Select(group => group with { Actions = [] })] };

            return Pending?.Task ?? Task.FromResult<HostResult<LayersPresentation>>(new HostResult<LayersPresentation>.Success(presentation));
        }

        public Task<HostResult<bool>> ClearAsync(CancellationToken cancellationToken)
        {
            ClearCount++;
            ClearToken = cancellationToken;

            if (ClearResult is HostResult<bool>.Success { Value: true })
            {
                SelectionTargets = [];
                EmphasisTargets = [];
            }

            ClearStarted.TrySetResult();
            return PendingClear?.Task ?? Task.FromResult(ClearResult);
        }

        public Task<string> EmphasizeObjectsAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken)
        {
            EmphasisTargets = objects;
            EmphasisToken = cancellationToken;

            return PendingEmphasis?.Task ?? Task.FromResult("Selection updated.");
        }

        public Task<string> FocusAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken)
        {
            HostCalls++;
            FocusCount++;
            FocusTargets = objects;
            FocusToken = cancellationToken;

            return PendingFocus?.Task ?? Task.FromResult(FocusMessage);
        }
    }
}
