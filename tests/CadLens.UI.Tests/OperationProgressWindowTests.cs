using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using CadLens.Common;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>
/// Exercises the independent progress dispatcher while the originating UI thread cannot process messages.
/// </summary>
[Collection("Language changes")]
public sealed class OperationProgressWindowTests
{
    /// <summary>
    /// The real Cancel button remains responsive while its originating dispatcher is synchronously blocked.
    /// </summary>
    [Fact]
    public void CancelButtonWorksWhileOriginatingDispatcherIsBlocked() => WpfTest.Run(() =>
    {
        var originatingDispatcher = Dispatcher.CurrentDispatcher;
        var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackThread = 0;
        var callbackCount = 0;
        Window window;

        using (var progress = new OperationProgressWindow(() =>
               {
                   callbackThread = Environment.CurrentManagedThreadId;
                   Interlocked.Increment(ref callbackCount);
                   canceled.TrySetResult(true);
               }))
        {
            window = GetWindow(progress);
            Assert.NotSame(originatingDispatcher, window.Dispatcher);
            progress.Report(new OperationProgress(OperationStage.Reading, 17));
            WaitFor(window, current => current.IsVisible);
            var click = Task.Run(() => window.Dispatcher.Invoke(() =>
                Find<Button>(window).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent))));

            Assert.True(
                canceled.Task.Wait(TimeSpan.FromSeconds(2)),
                "Cancel waited for the blocked originating dispatcher.");
            click.GetAwaiter().GetResult();
            Assert.Equal(window.Dispatcher.Thread.ManagedThreadId, callbackThread);
            Assert.NotEqual(Environment.CurrentManagedThreadId, callbackThread);
            progress.Report(new OperationProgress(OperationStage.Focusing, 99, 100));
            Thread.Sleep(150);
            window.Dispatcher.Invoke(() =>
            {
                Assert.True(window.IsVisible);
                Assert.False(Find<Button>(window).IsEnabled);
                Assert.Equal(UiText.Current.Get("Cancelling…"), Find<TextBlock>(window).Text);
                Assert.True(Find<ProgressBar>(window).IsIndeterminate);
                Find<Button>(window).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            });
            Assert.Equal(1, callbackCount);
            Assert.True(progress.IsCancellationRequested);
        }

        Assert.True(window.Dispatcher.HasShutdownFinished);
    });

    /// <summary>
    /// Sequential operations use distinct dispatchers, and closing keeps the cancelling window open.
    /// </summary>
    [Fact]
    public void SequentialSessionsKeepTheirOwnWindowLifetime()
    {
        Window firstWindow;
        var firstCancellations = 0;

        using (var first = new OperationProgressWindow(() => firstCancellations++))
        {
            firstWindow = GetWindow(first);
            first.Report(new OperationProgress(OperationStage.CreatingGraphics, 2, 8));
            WaitFor(
                firstWindow,
                window => window.IsVisible && Math.Abs(Find<ProgressBar>(window).Value - 2) < 0.001);
            firstWindow.Dispatcher.Invoke(() =>
            {
                Assert.False(Find<ProgressBar>(firstWindow).IsIndeterminate);
                Assert.Equal(8, Find<ProgressBar>(firstWindow).Maximum);
                var expected = OperationProgressWindow.FormatProgress(
                    new OperationProgress(OperationStage.CreatingGraphics, 2, 8));
                Assert.Equal(expected, Find<TextBlock>(firstWindow).Text);
            });
        }

        Assert.True(firstWindow.Dispatcher.HasShutdownFinished);
        Assert.Equal(0, firstCancellations);
        Window secondWindow;
        var secondCancellations = 0;

        using (var second = new OperationProgressWindow(() => secondCancellations++))
        {
            secondWindow = GetWindow(second);
            Assert.NotSame(firstWindow.Dispatcher, secondWindow.Dispatcher);
            WaitFor(secondWindow, window => window.IsVisible);

            secondWindow.Dispatcher.Invoke(secondWindow.Close);

            Assert.Equal(1, secondCancellations);
            Assert.True(second.IsCancellationRequested);
            secondWindow.Dispatcher.Invoke(() =>
            {
                Assert.True(secondWindow.IsVisible);
                Assert.False(secondWindow.Dispatcher.HasShutdownStarted);
                Assert.Equal(UiText.Current.Get("Cancelling…"), Find<TextBlock>(secondWindow).Text);
            });
        }

        Assert.True(secondWindow.Dispatcher.HasShutdownFinished);
        Assert.Equal(0, firstCancellations);
    }

    /// <summary>
    /// Disposing a short operation prevents its delayed window, and late callbacks cannot restart it.
    /// </summary>
    [Fact]
    public void DisposedSessionIgnoresLateReportsAndCancellation()
    {
        var cancellations = 0;
        var progress = new OperationProgressWindow(() => cancellations++);
        var window = GetWindow(progress);
        var shown = false;
        window.Dispatcher.Invoke(() => window.IsVisibleChanged += (_, _) => shown |= window.IsVisible);

        progress.Dispose();
        progress.Report(new OperationProgress(OperationStage.DisplayingGraphics, 100, 100));
        progress.RequestCancellation();
        progress.Dispose();

        Assert.True(window.Dispatcher.HasShutdownFinished);
        Assert.False(shown);
        Assert.Equal(0, cancellations);
    }

    /// <summary>
    /// A cancellation callback can dispose its own progress session without joining itself or reentering Close.
    /// </summary>
    [Fact]
    public void CancellationCanDisposeOnItsOwnDispatcher()
    {
        var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sessions = new OperationProgressWindow?[1];

        try
        {
            sessions[0] = new OperationProgressWindow(() =>
            {
                sessions[0]!.Dispose();
                canceled.TrySetResult(true);
            });
            var window = GetWindow(sessions[0]!);

            window.Dispatcher.Invoke(window.Close);

            Assert.True(SpinWait.SpinUntil(() => canceled.Task.IsCompleted, TimeSpan.FromSeconds(2)));
            Assert.True(SpinWait.SpinUntil(() => window.Dispatcher.HasShutdownFinished, TimeSpan.FromSeconds(2)));
        }
        finally
        {
            sessions[0]?.Dispose();
        }
    }

    /// <summary>
    /// Progress stage and item counts use the app language without changing the calling thread's culture.
    /// </summary>
    /// <param name="language">App display language.</param>
    [Theory]
    [InlineData(LanguagePreference.English)]
    [InlineData(LanguagePreference.Russian)]
    public void FormatsLocalizedStageAndCounts(LanguagePreference language)
    {
        var previous = UiText.Current.Preference;

        try
        {
            UiText.Current.Select(language, persist: false);
            var stage = UiText.Current.Get("Reading drawing objects…");
            var fraction = string.Format(
                UiText.Current.Culture,
                UiText.Current.Get("{0} {1:N0} of {2:N0}"),
                stage,
                3,
                10);
            var count = string.Format(UiText.Current.Culture, UiText.Current.Get("{0} {1:N0} processed"), stage, 17);

            Assert.Equal(
                fraction,
                OperationProgressWindow.FormatProgress(new OperationProgress(OperationStage.Reading, 3, 10)));
            Assert.Equal(
                count,
                OperationProgressWindow.FormatProgress(new OperationProgress(OperationStage.Reading, 17)));
            Assert.Equal(
                UiText.Current.Get("Regenerating drawing view…"),
                OperationProgressWindow.FormatProgress(new OperationProgress(OperationStage.Regenerating)));
        }
        finally
        {
            UiText.Current.Select(previous, persist: false);
        }
    }

    private static Window GetWindow(OperationProgressWindow progress) =>
        Assert.IsType<Window>(typeof(OperationProgressWindow)
            .GetField("_window", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(progress));

    private static T Find<T>(Window window) where T : FrameworkElement =>
        Assert.Single(
            WpfTest.Descendants(window).OfType<T>(),
            element => element.Name is "OperationProgressText" or "OperationProgressBar" or "CancelOperationButton");

    private static void WaitFor(Window window, Func<Window, bool> condition)
    {
        var elapsed = Stopwatch.StartNew();

        while (elapsed.Elapsed < TimeSpan.FromSeconds(3))
        {
            if (window.Dispatcher.Invoke(() => condition(window)))
                return;

            Thread.Sleep(20);
        }

        Assert.Fail("The independent progress dispatcher did not reach the expected state.");
    }
}