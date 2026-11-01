using System.Collections.Immutable;
using System.Reflection;
using CadLens.Common;
using CadLens.Lenses;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>Checks cancellation ownership across explorer actions and isolation cleanup.</summary>
[Collection("Language changes")]
public sealed class OperationCancellationTests
{
    /// <summary>Cancel waits for the action, clears isolation with a live token, and retains collected data.</summary>
    /// <param name="stage">Long running drawing action.</param>
    [Theory]
    [InlineData(OperationStage.Isolating)]
    [InlineData(OperationStage.Selecting)]
    [InlineData(OperationStage.Focusing)]
    public async Task CancelSettlesActionBeforeClearingIsolation(OperationStage stage)
    {
        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.ObjectTypes);
        await model.ActivateAsync(CancellationToken.None);
        await model.EnterCommand.ExecuteAsync(Assert.Single(model.Items));
        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        await model.ToggleAutoIsolationCommand.ExecuteAsync(null);
        var groups = model.Groups;
        var current = model.Current;
        var fields = model.CurrentFields;
        actions.PauseStage = stage;
        var command = stage switch
        {
            OperationStage.Isolating => model.IsolateCommand,
            OperationStage.Selecting => model.SelectCommand,
            _ => model.FocusCommand
        };
        var operation = command.ExecuteAsync(null);
        Assert.True(model.IsBusy);
        Assert.True(model.CancelCommand.CanExecute(null));
        model.CancelCommand.Execute(null);
        await WaitAsync(actions.Canceled.Task);

        Assert.False(operation.IsCompleted);
        Assert.True(model.IsBusy);
        Assert.Equal(0, actions.CancellationCleanupCount);
        actions.Release.SetResult(true);
        await WaitAsync(operation);

        Assert.False(model.IsBusy);
        Assert.False(model.IsAutoIsolation);
        Assert.Equal(1, actions.CancellationCleanupCount);
        Assert.False(actions.CleanupToken.CanBeCanceled);
        Assert.Same(current, model.Current);
        Assert.Equal(groups, model.Groups);
        Assert.Equal<DetailField>(fields, model.CurrentFields);
        Assert.Equal(1, actions.ReadCount);
        Assert.Null(model.OperationProgress);
        Assert.False(model.CancelCommand.CanExecute(null));
        Assert.True(model.ReadCommand.CanExecute(null));
        Assert.Equal(UiText.Current.Get("Operation canceled. Isolation cleared. Auto Isolate off."), model.Status);
        actions.Progress!.Report(new OperationProgress(stage, 10, 10));
        Assert.Null(model.OperationProgress);
    }

    /// <summary>Finishing an operation keeps its token source alive until an in-flight Cancel callback returns.</summary>
    [Fact]
    public async Task TokenOutlivesProgressCancellationCallback()
    {
        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.ObjectTypes);
        await model.ActivateAsync(CancellationToken.None);
        await model.EnterCommand.ExecuteAsync(Assert.Single(model.Items));
        actions.PauseStage = OperationStage.Selecting;
        var operation = model.SelectCommand.ExecuteAsync(null);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var request = Assert.IsType<CancellationTokenSource>(typeof(ObjectExplorerViewModel)
            .GetField("_pendingRequest", flags)!.GetValue(model));
        var window = Assert.IsType<OperationProgressWindow>(typeof(ObjectExplorerViewModel)
            .GetField("_operationWindow", flags)!.GetValue(model));
        var closed = Assert.IsAssignableFrom<Task>(typeof(OperationProgressWindow)
            .GetProperty("Closed", flags)!.GetValue(window));
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
#if NETFRAMEWORK
        using var registration = request.Token.Register(PauseCancellation);
#else
        await using var registration = request.Token.Register(PauseCancellation);
#endif

        try
        {
            model.CancelCommand.Execute(null);
            await WaitAsync(entered.Task);
            actions.Release.TrySetResult(true);
            await WaitAsync(operation);

            Assert.False(model.IsBusy);
            Assert.False(closed.IsCompleted);
            Assert.True(request.Token.IsCancellationRequested);
        }
        finally
        {
            release.TrySetResult(true);
            actions.Release.TrySetResult(true);
        }

        await WaitAsync(closed);
        Assert.True(SpinWait.SpinUntil(
            () => Record.Exception(() => _ = request.Token) is ObjectDisposedException,
            TimeSpan.FromSeconds(3)));
        return;

        void PauseCancellation()
        {
            entered.TrySetResult(true);
            SpinWait.SpinUntil(() => release.Task.IsCompleted, TimeSpan.FromSeconds(10));
        }
    }

    /// <summary>A cleanup exception is reported and does not leave the explorer permanently busy.</summary>
    [Fact]
    public async Task CleanupFailureLeavesExplorerUsable()
    {
        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.ObjectTypes);
        await model.ActivateAsync(CancellationToken.None);
        await model.EnterCommand.ExecuteAsync(Assert.Single(model.Items));
        actions.PauseStage = OperationStage.Isolating;
        actions.FailCleanup = true;
        var operation = model.IsolateCommand.ExecuteAsync(null);
        model.CancelCommand.Execute(null);
        await WaitAsync(actions.Canceled.Task);
        actions.Release.SetResult(true);
        await WaitAsync(operation);

        Assert.Contains("fixture cleanup", model.Status);
        Assert.False(model.IsBusy);
        Assert.False(model.IsAutoIsolation);
        Assert.True(model.ReadCommand.CanExecute(null));
    }

    /// <summary>Changing scope commits only after its new inventory is ready.</summary>
    [Fact]
    public async Task CancelingScopeChangeKeepsCollectedDetails()
    {
        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.ObjectTypes);
        await model.ActivateAsync(CancellationToken.None);
        await model.EnterCommand.ExecuteAsync(Assert.Single(model.Items));
        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        var current = model.Current;
        var groups = model.Groups;
        var fields = model.CurrentFields;
        actions.PauseStage = OperationStage.Reading;
        var operation = model.ShowSelectedObjectsCommand.ExecuteAsync(null);
        Assert.True(model.IsAllObjects);
        model.CancelCommand.Execute(null);
        await WaitAsync(actions.Canceled.Task);
        actions.Release.SetResult(true);
        await WaitAsync(operation);

        Assert.True(model.IsAllObjects);
        Assert.Same(current, model.Current);
        Assert.Equal(groups, model.Groups);
        Assert.Equal<DetailField>(fields, model.CurrentFields);
        Assert.False(model.IsBusy);
        Assert.Equal(1, actions.CancellationCleanupCount);
    }

    private static async Task WaitAsync(Task task)
    {
        Assert.Same(task, await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10))));
        await task;
    }

    private sealed record LayerId(string DisplayId) : ILayerId;

    private sealed class Actions : IObjectExplorerActions
    {
        public Task<HostResult<bool>> PaintAsync(
            ImmutableArray<PaintGroup> groups,
            CancellationToken cancellationToken,
            IProgress<OperationProgress>? progress = null) =>
            Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(true));

        internal OperationStage? PauseStage { get; set; }
        internal TaskCompletionSource<bool> Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal IProgress<OperationProgress>? Progress { get; private set; }
        internal CancellationToken CleanupToken { get; private set; }
        internal int CancellationCleanupCount { get; private set; }
        internal int ReadCount { get; private set; }
        internal bool FailCleanup { get; set; }

        public async Task<HostResult<LensPresentation>> ReadAsync(
            DrawingGrouping grouping,
            IReadOnlyCollection<string> enabledFilters,
            ImmutableArray<IPlacedObjectId>? selectedObjects,
            CancellationToken cancellationToken,
            int? maximumObjects = null,
            IProgress<DrawingReadProgress>? progress = null)
        {
            ReadCount++;
            progress?.Report(new DrawingReadProgress(0, 1, 0));
            await PauseAsync(OperationStage.Reading, cancellationToken, null);
            var layer = new LayerId("Layer");
            var inventory = new DrawingInventory(
                "Model",
                [new LayerSnapshot(layer, "Layer", false, false, false, false)],
                [new EntitySnapshot(new TestEntityId(1), layer, "AcDbLine")]);
            return new HostResult<LensPresentation>.Success(
                DrawingLensProvider.Build(inventory, grouping, enabledFilters));
        }

        public Task<HostResult<LensPresentation>> ReadBlockContentsAsync(
            IPlacedObjectId block,
            IReadOnlyCollection<string> enabledFilters,
            CancellationToken cancellationToken,
            IProgress<DrawingReadProgress>? progress = null) => throw new NotSupportedException();

        public Task<HostResult<ImmutableArray<IPlacedObjectId>>> RequestObjectsAsync(
            CancellationToken cancellationToken,
            IProgress<OperationProgress>? progress = null) =>
            Task.FromResult<HostResult<ImmutableArray<IPlacedObjectId>>>(
                new HostResult<ImmutableArray<IPlacedObjectId>>.Success([new TestEntityId(1)]));

        public Task<HostResult<bool>> ExistsAsync(
            IPlacedObjectId target,
            CancellationToken cancellationToken,
            IProgress<OperationProgress>? progress = null) => Task.FromResult<HostResult<bool>>(
            new HostResult<bool>.Success(true));

        public void ClearImmediately(bool hostTerminating)
        {
        }

        public async Task<HostResult<bool>> SelectAsync(
            ImmutableArray<IPlacedObjectId> objects,
            CancellationToken cancellationToken,
            IProgress<OperationProgress>? progress = null)
        {
            await PauseAsync(OperationStage.Selecting, cancellationToken, progress);
            return new HostResult<bool>.Success(true);
        }

        public Task<HostResult<bool>> ClearIsolationAsync(
            CancellationToken cancellationToken,
            IProgress<OperationProgress>? progress = null)
        {
            if (!Canceled.Task.IsCompleted)
                return Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(true));

            CancellationCleanupCount++;
            CleanupToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();

            return FailCleanup
                ? throw new InvalidOperationException("fixture cleanup")
                : Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(true));
        }

        public async Task<string> IsolateObjectsAsync(
            ImmutableArray<IPlacedObjectId> objects,
            CancellationToken cancellationToken,
            IProgress<OperationProgress>? progress = null)
        {
            await PauseAsync(OperationStage.Isolating, cancellationToken, progress);
            return "Isolated.";
        }

        public Task<HostResult<bool>> ClearAsync(
            CancellationToken cancellationToken,
            IProgress<OperationProgress>? progress = null) => Task.FromResult<HostResult<bool>>(
            new HostResult<bool>.Success(true));

        public async Task<string> FocusAsync(
            ImmutableArray<IPlacedObjectId> objects,
            CancellationToken cancellationToken,
            IProgress<OperationProgress>? progress = null)
        {
            await PauseAsync(OperationStage.Focusing, cancellationToken, progress);
            return "Focused.";
        }

        private async Task PauseAsync(
            OperationStage stage,
            CancellationToken cancellationToken,
            IProgress<OperationProgress>? progress)
        {
            if (PauseStage != stage)
                return;

            Progress = progress;
            progress?.Report(new OperationProgress(stage, 2, 10));
#if NETFRAMEWORK
            using var registration = cancellationToken.Register(() => Canceled.TrySetResult(true));
#else
            await using var registration = cancellationToken.Register(() => Canceled.TrySetResult(true));
#endif
            await Release.Task;
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}