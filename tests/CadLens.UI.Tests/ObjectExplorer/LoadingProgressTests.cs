using System.Collections.Immutable;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CadLens.Common;
using CadLens.Lenses;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>
/// Checks startup options and drawing read progress through cancellation and repeated reads.
/// </summary>
[Collection("Language changes")]
public sealed class LoadingProgressTests
{
    /// <summary>
    /// Options and restored choices remain available before, during, and after a refused automatic read.
    /// </summary>
    /// <param name="grouping">Lens organization.</param>
    /// <param name="restoreEnabled">Whether inclusion choices were saved in the previous session.</param>
    [Theory]
    [InlineData(DrawingGrouping.Layers, false)]
    [InlineData(DrawingGrouping.Layers, true)]
    [InlineData(DrawingGrouping.ObjectTypes, false)]
    [InlineData(DrawingGrouping.ObjectTypes, true)]
    public void OptionsExistBeforeFirstRead(DrawingGrouping grouping, bool restoreEnabled) => Run(() =>
    {
        using var settings = new SettingsFile();
        var ids = DrawingLensProvider.GetFilters(grouping).Select(filter => filter.Id).ToArray();

        if (restoreEnabled)
        {
            var fileName = grouping == DrawingGrouping.Layers ? "lens-layers.json" : "lens-object-types.json";
            settings.Service.Save(fileName, new {EnabledFilters = ids});
        }

        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, grouping, settings.Service);
        Assert.Equal(grouping == DrawingGrouping.Layers ? 3 : 2, model.Filters.Length);
        Assert.Equal(ids, model.Filters.Select(filter => filter.Descriptor.Id));
        Assert.All(model.Filters, filter => Assert.Equal(restoreEnabled, filter.IsEnabled));
        Assert.Equal(0, actions.ReadCount);

        var loading = model.ActivateAsync(CancellationToken.None);

        Assert.True(model.IsBusy);
        Assert.Equal(ids, model.Filters.Select(filter => filter.Descriptor.Id));
        Assert.All(model.Filters, filter => Assert.Equal(restoreEnabled, filter.IsEnabled));
        actions.Complete();
        Finish(loading);
        Assert.Contains("Large drawing", model.EmptyMessage);
        Assert.Equal(ids, model.Filters.Select(filter => filter.Descriptor.Id));
        Assert.All(model.Filters, filter => Assert.Equal(restoreEnabled, filter.IsEnabled));
        Assert.All(model.Filters, filter => Assert.True(model.ToggleFilterCommand.CanExecute(filter)));
    });

    /// <summary>
    /// The bottom bar switches from preparation to real progress and clears after a refused read.
    /// </summary>
    [Fact]
    public void ProgressBarTracksReadOnTheUiThread() => Run(() =>
    {
        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.Layers);
        var view = new ObjectExplorerView(model);
        view.Measure(new Size(300, 660));
        view.Arrange(new Rect(0, 0, 300, 660));
        view.UpdateLayout();
        var bar = Assert.IsType<ProgressBar>(view.FindName("DrawingReadProgressBar"));
        Assert.Equal(Visibility.Collapsed, bar.Visibility);
        var loading = model.ActivateAsync(CancellationToken.None);
        Pump();
        Assert.True(model.IsReading);
        Assert.Equal(Visibility.Visible, bar.Visibility);
        Assert.True(bar.IsIndeterminate);
        Assert.Equal(3, bar.Height);
        Assert.Equal(3, Grid.GetRow(bar));
        var uiThread = Environment.CurrentManagedThreadId;
        var updateThread = 0;
        model.PropertyChanged += CaptureUpdateThread;
        var report = new DrawingReadProgress(2, 10, 50);

        Task.Run(() => actions.Progress!.Report(report)).GetAwaiter().GetResult();
        Pump();

        Assert.Equal(uiThread, updateThread);
        Assert.Equal(report, model.ReadProgress);
        Assert.False(bar.IsIndeterminate);
        Assert.Equal(10, bar.Maximum);
        Assert.Equal(2, bar.Value);
        var expectedText = string.Format(UiText.Current.Culture, UiText.Current.Get("Processed {0:N0} objects"), 50);
        Assert.Equal(expectedText, bar.ToolTip);
        Assert.Equal(expectedText, model.Status);
        actions.Progress!.Report(new DrawingReadProgress(0, 1, 200));
        Pump();
        Assert.True(bar.IsIndeterminate);
        expectedText = string.Format(UiText.Current.Culture, UiText.Current.Get("Processed {0:N0} objects"), 200);
        Assert.Equal(expectedText, model.Status);
        actions.Complete();
        Finish(loading);
        Assert.False(model.IsReading);
        Assert.Null(model.ReadProgress);
        Assert.Equal(Visibility.Collapsed, bar.Visibility);
        model.PropertyChanged -= CaptureUpdateThread;
        return;

        void CaptureUpdateThread(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(ObjectExplorerViewModel.ReadProgress))
                updateThread = Environment.CurrentManagedThreadId;
        }
    });

    /// <summary>
    /// Reports from a completed read cannot overwrite a new read or revive completed progress.
    /// </summary>
    [Fact]
    public void CompletedReadReportsAreIgnored() => Run(() =>
    {
        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.Layers);
        var activation = model.ActivateAsync(CancellationToken.None);
        var oldProgress = actions.Progress!;
        actions.Complete();
        Finish(activation);
        oldProgress.Report(new DrawingReadProgress(9, 10, 90));
        Pump();
        Assert.Null(model.ReadProgress);
        actions.Pending = new TaskCompletionSource<HostResult<LensPresentation>>();
        var loading = model.ReadCommand.ExecuteAsync(null);
        var current = new DrawingReadProgress(1, 4, 5);
        actions.Progress!.Report(current);
        oldProgress.Report(new DrawingReadProgress(10, 10, 100));
        Pump();

        Assert.Equal(current, model.ReadProgress);
        actions.Complete();
        Finish(loading);
        Assert.Null(model.ReadProgress);
    });

    /// <summary>
    /// Queued reports cannot survive collapse, reopening, or closing the panel.
    /// </summary>
    [Fact]
    public void CanceledReadReportsAreIgnoredAfterReopening() => Run(() =>
    {
        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.Layers);
        var activation = model.ActivateAsync(CancellationToken.None);
        var canceledProgress = actions.Progress!;
        canceledProgress.Report(new DrawingReadProgress(1, 2, 3));
        var collapse = model.DeactivateAsync(CancellationToken.None);
        Pump();
        Assert.False(model.IsReading);
        actions.Complete();
        Finish(Task.WhenAll(activation, collapse));
        actions.Pending = new TaskCompletionSource<HostResult<LensPresentation>>();
        var reopening = model.ActivateAsync(CancellationToken.None);
        canceledProgress.Report(new DrawingReadProgress(2, 2, 30));
        Pump();
        Assert.Equal(new DrawingReadProgress(0, 0, 0), model.ReadProgress);
        var currentProgress = actions.Progress!;
        currentProgress.Report(new DrawingReadProgress(1, 4, 8));
        model.Close(false);
        Pump();
        Assert.False(model.IsReading);
        actions.Complete();
        Finish(reopening);
        Assert.Null(model.ReadProgress);
    });

    private static void Run(Action action) => WpfTest.Run(() =>
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());

        try
        {
            action();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    });

    private static void Finish(Task task)
    {
        Pump();
        Assert.True(task.IsCompleted);
        task.GetAwaiter().GetResult();
        Pump();
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private sealed class Actions : IObjectExplorerActions
    {
        internal TaskCompletionSource<HostResult<LensPresentation>> Pending { get; set; } = new();
        internal IProgress<DrawingReadProgress>? Progress { get; private set; }
        internal int ReadCount { get; private set; }

        internal void Complete() => Pending.SetResult(new HostResult<LensPresentation>.Unavailable(
            "Large drawing. Use Refresh to load all objects, or choose Selected objects."));

        public Task<HostResult<LensPresentation>> ReadAsync(
            DrawingGrouping grouping,
            IReadOnlyCollection<string> enabledFilters,
            ImmutableArray<IPlacedObjectId>? selectedObjects,
            CancellationToken cancellationToken,
            int? maximumObjects = null,
            IProgress<DrawingReadProgress>? progress = null)
        {
            ReadCount++;
            Progress = progress;
            return Pending.Task;
        }

        public Task<HostResult<LensPresentation>> ReadBlockContentsAsync(
            IPlacedObjectId block,
            IReadOnlyCollection<string> enabledFilters,
            CancellationToken cancellationToken,
            IProgress<DrawingReadProgress>? progress = null) =>
            throw new NotSupportedException();

        public Task<HostResult<ImmutableArray<IPlacedObjectId>>> RequestObjectsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<HostResult<ImmutableArray<IPlacedObjectId>>>(
                new HostResult<ImmutableArray<IPlacedObjectId>>.Success([]));

        public void ClearImmediately(bool hostTerminating)
        {
        }

        public Task<HostResult<bool>> SelectAsync(
            ImmutableArray<IPlacedObjectId> objects,
            CancellationToken cancellationToken) =>
            ClearAsync(cancellationToken);

        public Task<HostResult<bool>> ClearIsolationAsync(CancellationToken cancellationToken) =>
            ClearAsync(cancellationToken);

        public Task<string> IsolateObjectsAsync(
            ImmutableArray<IPlacedObjectId> objects,
            CancellationToken cancellationToken) =>
            Task.FromResult("");

        public Task<HostResult<bool>> ClearAsync(CancellationToken cancellationToken) =>
            Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(true));

        public Task<string> FocusAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken) =>
            Task.FromResult("");
    }
}