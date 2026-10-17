using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using CadLens.Common;
using CadLens.Lenses;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>
/// Exercises the independent progress dispatcher while the originating UI thread cannot process messages.
/// </summary>
[Collection("Language changes")]
public sealed class OperationProgressWindowTests
{
    /// <summary>A short session starts and stops while the host owns WPF theme resources.</summary>
    [Fact]
    public void ShortSessionDoesNotWaitForHostThemeResources() => WpfTest.Run(() =>
    {
        var resources = typeof(Window).Assembly.GetType("System.Windows.SystemResources")!;
        var themeLock = resources.GetProperty("ThemeDictionaryLock", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;
        Task session;
        bool ready;

        lock (themeLock)
        {
            session = Task.Run(() =>
            {
                var progress = new OperationProgressWindow(() => { });
                var dispatcher = GetDispatcher(progress);
                progress.Dispose();
                WaitForShutdown(dispatcher);
            });
            ready = session.Wait(TimeSpan.FromSeconds(2));
        }

        session.GetAwaiter().GetResult();
        Assert.True(ready, "Progress lifetime waited for the host's WPF theme resource lock.");
    });

    /// <summary>Cancel works before window creation and the delayed window retains the cancelling state.</summary>
    [Fact]
    public void CancellationBeforeWindowCreationRemainsVisible()
    {
        var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var progress = new OperationProgressWindow(() => canceled.TrySetResult(true));
        Assert.Null(WindowField.GetValue(progress));

        progress.Report(new OperationProgress(OperationStage.WaitingForSelection));
        progress.RequestCancellation();

        Assert.True(SpinWait.SpinUntil(() => canceled.Task.IsCompleted, TimeSpan.FromSeconds(2)));
        var window = GetWindow(progress);
        window.Dispatcher.Invoke(() =>
        {
            Assert.False(Find<Button>(window).IsEnabled);
            Assert.Equal(
                UiText.Current.Get("Cancelling… Press Esc in AutoCAD to finish object selection."),
                Find<TextBlock>(window).Text);
        });
    }

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

        WaitForShutdown(window.Dispatcher);
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

        WaitForShutdown(firstWindow.Dispatcher);
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

        WaitForShutdown(secondWindow.Dispatcher);
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
        var dispatcher = GetDispatcher(progress);
        Assert.Null(WindowField.GetValue(progress));

        progress.Dispose();
        progress.Report(new OperationProgress(OperationStage.DisplayingGraphics, 100, 100));
        progress.RequestCancellation();
        progress.Dispose();

        WaitForShutdown(dispatcher);
        Assert.Null(WindowField.GetValue(progress));
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

    /// <summary>
    /// The independent window matches the actual owner's effective colors and recreates its local controls.
    /// </summary>
    /// <param name="theme">Explicit or host-following theme.</param>
    /// <param name="palette">Owner's surface palette.</param>
    /// <param name="accent">Owner's accent.</param>
    /// <param name="hostIsLight">Current AutoCAD theme supplied to the owner.</param>
    [Theory]
    [InlineData("Light", "Paper", "Amber", false)]
    [InlineData("Dark", "Graphite", "Violet", true)]
    [InlineData("Follow AutoCAD", "Quiet", "Mint", true)]
    [InlineData("Follow AutoCAD", "Quiet", "Mint", false)]
    public void UsesOwnersEffectiveAppearance(string theme, string palette, string accent, bool hostIsLight) =>
        WpfTest.Run(() =>
        {
            using var settings = new SettingsFile();
            var appearance = new AppearancePreferences(settings.Service)
            {
                Theme = theme, Palette = palette, Accent = accent
            };
            WithPendingSelection(appearance, hostIsLight, null, (owner, worker) =>
            {
                AssertMatchingAppearance(owner, worker);

                if (theme is not ("Light" or "Dark"))
                    return;

                var previewName = $"operation-progress-{theme.ToLowerInvariant()}";
                worker.Dispatcher.Invoke(() => SavePreview(worker, previewName));
            });
        });

    /// <summary>
    /// An operation captures the latest session palette even when persisting the changed choices fails.
    /// </summary>
    [Fact]
    public void UsesLatestUnsavedAppearance() => WpfTest.Run(() =>
    {
        using var settings = new SettingsFile();
        var appearance = new AppearancePreferences(settings.Service) {Theme = "Dark"};
        var appearancePath = settings.Path;
        Color initialBackground = default;
        WithPendingSelection(
            appearance,
            false,
            owner =>
            {
                initialBackground = Brush(owner, "QuietBackground").Color;
                File.Delete(appearancePath);
                Directory.CreateDirectory(appearancePath);
                owner.Appearance.Theme = "Light";
                owner.Appearance.Palette = "Paper";
                owner.Appearance.Accent = "Amber";
                Assert.True(owner.Appearance.SaveFailed);
            },
            (owner, worker) =>
            {
                Assert.NotEqual(initialBackground, Brush(owner, "QuietBackground").Color);
                AssertMatchingAppearance(owner, worker);
            });
    });

    /// <summary>
    /// The popup centers over the owner's latest visible position after its view was already constructed.
    /// </summary>
    [Fact]
    public void CentersOverOwnersLatestPosition() => WpfTest.Run(() =>
    {
        using var settings = new SettingsFile();
        var appearance = new AppearancePreferences(settings.Service);
        WithPendingSelection(
            appearance,
            false,
            owner =>
            {
                var workArea = SystemParameters.WorkArea;
                owner.ShowActivated = false;
                owner.Left = workArea.Left + 10;
                owner.Top = workArea.Top + 10;
                owner.Show();
                owner.UpdateLayout();
                var initialBounds = Bounds(owner);
                workArea = WorkArea(owner);
                owner.Left = workArea.Left + (workArea.Width - owner.ActualWidth) / 2;
                owner.Top = workArea.Top + (workArea.Height - owner.ActualHeight) / 2;
                owner.UpdateLayout();
                Assert.NotEqual(initialBounds, Bounds(owner));
            },
            (owner, worker) =>
            {
                var ownerBounds = Bounds(owner);
                Assert.NotSame(owner.Dispatcher, worker.Dispatcher);
                WaitFor(worker, window => window.IsLoaded);
                worker.Dispatcher.Invoke(() =>
                {
                    var actual = Bounds(worker);
                    Assert.Equal(WindowStartupLocation.Manual, worker.WindowStartupLocation);
                    var horizontalError = Math.Abs(
                        actual.Left + actual.Width / 2 - ownerBounds.Left - ownerBounds.Width / 2);
                    var verticalError = Math.Abs(
                        actual.Top + actual.Height / 2 - ownerBounds.Top - ownerBounds.Height / 2);
                    Assert.True(
                        horizontalError <= 2 && verticalError <= 2,
                        $"Owner: {ownerBounds}; popup: {actual}; center error: {horizontalError}, {verticalError}.");
                });
            });
    });

    /// <summary>
    /// A popup wider than its owner remains entirely inside the monitor work area near its bottom-right edge.
    /// </summary>
    [Fact]
    public void ClampsCenteredPopupInsideOwnersWorkArea() => WpfTest.Run(() =>
    {
        using var settings = new SettingsFile();
        var appearance = new AppearancePreferences(settings.Service);
        WithPendingSelection(
            appearance,
            false,
            owner =>
            {
                owner.ShowActivated = false;
                owner.Show();
                owner.Width = owner.MinWidth;
                owner.UpdateLayout();
                var workArea = WorkArea(owner);
                owner.Left = workArea.Right - owner.ActualWidth;
                owner.Top = workArea.Bottom - owner.ActualHeight;
                owner.UpdateLayout();
            },
            (owner, worker) =>
            {
                var ownerBounds = Bounds(owner);
                var workArea = WorkArea(owner);
                WaitFor(worker, window => window.IsLoaded);
                worker.Dispatcher.Invoke(() =>
                {
                    var actual = Bounds(worker);
                    var unclampedRight = ownerBounds.Left + ownerBounds.Width / 2 + actual.Width / 2;
                    Assert.True(unclampedRight > workArea.Right);
                    Assert.InRange(Math.Abs(actual.Right - workArea.Right), 0, 2);
                    Assert.True(actual.Left >= workArea.Left - 2);
                    Assert.True(actual.Top >= workArea.Top - 2);
                    Assert.True(actual.Right <= workArea.Right + 2);
                    Assert.True(actual.Bottom <= workArea.Bottom + 2);
                });
            });
    });

    private static Rect Bounds(Window window) =>
        new(window.Left, window.Top, window.ActualWidth, window.ActualHeight);

    private static Rect WorkArea(ExplorerWindow window) =>
        Assert.IsType<Rect>(typeof(ExplorerWindow)
            .GetMethod("GetMonitorWorkArea", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, null));

    private static void WithPendingSelection(
        AppearancePreferences appearance,
        bool hostIsLight,
        Action<ExplorerWindow>? beforeOperation,
        Action<ExplorerWindow, Window> inspect)
    {
        var previousContext = SynchronizationContext.Current;
        var actions = new PropertyFilterTests.Actions();
        using var lens = new ObjectExplorerLens(actions, DrawingGrouping.ObjectTypes);
        using var shell = new ExplorerViewModel([lens]);
        var owner = new ExplorerWindow(shell, appearance);
        Task? operation = null;

        try
        {
            owner.SetHostTheme(hostIsLight);
            shell.ToggleLensCommand.ExecuteAsync(shell.Lenses[0]).GetAwaiter().GetResult();
            var model = lens.ViewModel;
            model.EnterCommand.ExecuteAsync(Assert.Single(model.Items)).GetAwaiter().GetResult();
            model.EnterCommand.ExecuteAsync(model.Items[0]).GetAwaiter().GetResult();
            beforeOperation?.Invoke(owner);
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            actions.PendingSelection = new TaskCompletionSource<HostResult<bool>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            operation = model.SelectCommand.ExecuteAsync(null);
            var progress = Assert.IsType<OperationProgressWindow>(typeof(ObjectExplorerViewModel)
                .GetField("_operationWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(model));
            var worker = GetWindow(progress);
            progress.Report(new OperationProgress(OperationStage.Selecting, 64, 128));
            WaitFor(worker, window => window.IsVisible);

            inspect(owner, worker);
        }
        finally
        {
            actions.PendingSelection?.TrySetResult(new HostResult<bool>.Success(true));
            if (operation is {IsCompleted: false})
            {
                var frame = new DispatcherFrame();
                _ = operation.ContinueWith(
                    _ => frame.Continue = false,
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.FromCurrentSynchronizationContext());
                Dispatcher.PushFrame(frame);
            }

            operation?.GetAwaiter().GetResult();
            owner.Close();
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static void AssertMatchingAppearance(ExplorerWindow owner, Window worker)
    {
        string[] keys =
        [
            "QuietBackground", "QuietHeader", "QuietSurface", "QuietText", "QuietBorder", "QuietAccent",
            "QuietHover", "QuietDisabledText", "QuietDisabledSurface"
        ];
        var expected = keys.ToDictionary(key => key, key => Brush(owner, key).Color);
        var ownerResources = owner.Resources;
        var ownerButtonStyle = Assert.IsType<Style>(owner.FindResource("QuietButton"));
        Assert.NotSame(owner.Dispatcher, worker.Dispatcher);
        worker.Dispatcher.Invoke(() =>
        {
            worker.UpdateLayout();
            Assert.NotSame(ownerResources, worker.Resources);
            Assert.Equal(WindowStyle.None, worker.WindowStyle);
            var chrome = WindowChrome.GetWindowChrome(worker);
            Assert.NotNull(chrome);
            Assert.Equal(50, chrome.CaptionHeight);
            Assert.Equal(new CornerRadius(12), chrome.CornerRadius);

            foreach (var pair in expected)
                Assert.Equal(pair.Value, Brush(worker, pair.Key).Color);

            Assert.Equal(expected["QuietBackground"], Assert.IsType<SolidColorBrush>(worker.Background).Color);
            Assert.Equal(expected["QuietText"], Assert.IsType<SolidColorBrush>(worker.Foreground).Color);
            Assert.Equal(
                expected["QuietAccent"],
                Assert.IsType<SolidColorBrush>(Find<ProgressBar>(worker).Foreground).Color);
            var cancel = Find<Button>(worker);
            var close = Assert.Single(
                WpfTest.Descendants(worker).OfType<Button>(),
                button => button.Name == "OperationProgressCloseButton");
            var workerStyle = Assert.IsType<Style>(worker.FindResource("QuietButton"));
            Assert.NotSame(ownerButtonStyle, workerStyle);
            Assert.Same(workerStyle, cancel.Style);
            Assert.Same(workerStyle, close.Style);
            cancel.ApplyTemplate();
            Assert.NotNull(cancel.Template.FindName("Surface", cancel));
            Assert.Equal(expected["QuietText"], Assert.IsType<SolidColorBrush>(cancel.Foreground).Color);
            Assert.Equal(expected["QuietBorder"], Assert.IsType<SolidColorBrush>(cancel.BorderBrush).Color);
        });
    }

    private static SolidColorBrush Brush(FrameworkElement scope, string key) =>
        Assert.IsType<SolidColorBrush>(scope.FindResource(key));

    private static void SavePreview(Window window, string name)
    {
        var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(
            (int) Math.Ceiling(content.ActualWidth),
            (int) Math.Ceiling(content.ActualHeight),
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(AppContext.BaseDirectory, name + ".png"));
        encoder.Save(output);
    }

    private static readonly FieldInfo WindowField = typeof(OperationProgressWindow)
        .GetField("_window", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static Window GetWindow(OperationProgressWindow progress)
    {
        Window? window = null;
        Assert.True(SpinWait.SpinUntil(() =>
        {
            window = WindowField.GetValue(progress) as Window;
            return window is not null;
        }, TimeSpan.FromSeconds(3)), "The delayed progress window was not created.");
        return window!;
    }

    private static Dispatcher GetDispatcher(OperationProgressWindow progress) =>
        Assert.IsType<Dispatcher>(typeof(OperationProgressWindow)
            .GetField("_dispatcher", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(progress));

    private static void WaitForShutdown(Dispatcher dispatcher) =>
        Assert.True(SpinWait.SpinUntil(() => dispatcher.HasShutdownFinished, TimeSpan.FromSeconds(3)));

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