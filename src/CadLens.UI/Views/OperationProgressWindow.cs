using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using CadLens.Common;

namespace CadLens.UI;

/// <summary>
/// Shows cancellable drawing progress on an independent UI thread while the host dispatcher is busy.
/// </summary>
public sealed class OperationProgressWindow : IProgress<OperationProgress>, IDisposable
{
    private const int ShowDelayMilliseconds = 400;
    private readonly Action _requestCancellation;
    private readonly (string Key, Color Color)[] _palette;
    private readonly Rect? _ownerBounds;
    private readonly Rect _workArea;
    private readonly Thread _thread;
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private OperationProgress _latestProgress = new(OperationStage.Preparing);
    private Window? _window;
    private Action? _cancelOnDispatcher;
    private int _cancellationRequested;
    private int _disposed;

    /// <summary>
    /// Starts a private dispatcher without requiring the host UI thread to process messages.
    /// </summary>
    /// <param name="requestCancellation">Requests cancellation without waiting for the host dispatcher.</param>
    /// <param name="appearance">Calling view's effective palette, captured before starting the private dispatcher.</param>
    /// <param name="owner">Calling window whose current screen bounds determine the progress window's placement.</param>
    public OperationProgressWindow(
        Action requestCancellation,
        ResourceDictionary? appearance = null,
        Window? owner = null)
    {
        _requestCancellation = requestCancellation;
        _palette = CapturePalette(appearance);

        if (owner is {IsVisible: true})
        {
            _ownerBounds = new Rect(owner.Left, owner.Top, owner.ActualWidth, owner.ActualHeight);
            _workArea = owner is ExplorerWindow explorer ? explorer.GetMonitorWorkArea() : SystemParameters.WorkArea;
        }

        _thread = new Thread(RunWindow) {IsBackground = true, Name = "CAD Lens operation progress"};
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        try
        {
            _ready.Task.GetAwaiter().GetResult();
        }
        catch
        {
            _thread.Join();
            throw;
        }
    }

    /// <summary>
    /// Stores the latest progress from any thread for the private dispatcher to display.
    /// </summary>
    /// <param name="value">Immutable operation progress.</param>
    public void Report(OperationProgress value)
    {
        if (Volatile.Read(ref _disposed) == 0)
            Interlocked.Exchange(ref _latestProgress, value);
    }

    /// <summary>
    /// Whether the user requested cancellation through this operation's progress window.
    /// </summary>
    public bool IsCancellationRequested => Volatile.Read(ref _cancellationRequested) != 0;

    /// <summary>
    /// Requests cancellation once on the private UI thread and retains the window until disposal.
    /// </summary>
    public void RequestCancellation()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        var dispatcher = _window!.Dispatcher;

        if (dispatcher.CheckAccess())
            _cancelOnDispatcher!();
        else if (!dispatcher.HasShutdownStarted)
            dispatcher.BeginInvoke(DispatcherPriority.Send, _cancelOnDispatcher!);
    }

    /// <summary>
    /// Formats the current stage and available item counts in the app language.
    /// </summary>
    /// <param name="progress">Current operation stage and counts.</param>
    public static string FormatProgress(OperationProgress progress)
    {
        var stage = new UiMessage(StageText(progress.Stage));

        if (progress.Total > 0)
            return new UiMessage("{0} {1:N0} of {2:N0}", stage, progress.Completed, progress.Total).ToString();

        return progress.Completed > 0
            ? new UiMessage("{0} {1:N0} processed", stage, progress.Completed).ToString()
            : stage.ToString();
    }

    /// <summary>
    /// Closes this operation's window and waits for its private dispatcher to finish.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            var window = _window!;
            var dispatcher = window.Dispatcher;

            if (!dispatcher.HasShutdownStarted)
                dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(window.Close));
        }

        // A cancellation callback may dispose its own window while a Closing event is still unwinding.
        if (Thread.CurrentThread != _thread)
            _thread.Join();
    }

    private void RunWindow()
    {
        DispatcherTimer? timer = null;

        try
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var window = CreateWindow(out var status, out var progressBar, out var cancelButton);
            _window = window;
            _cancelOnDispatcher = () => CancelOnDispatcher(status, progressBar, cancelButton);
            cancelButton.Click += (_, _) => RequestCancellation();
            window.Closing += OnWindowClosing;
            timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            window.Closed += (_, _) =>
            {
                timer.Stop();
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            };
            var elapsed = Stopwatch.StartNew();
            timer.Tick += (_, _) =>
            {
                if (Volatile.Read(ref _disposed) != 0)
                    return;

                if (Volatile.Read(ref _cancellationRequested) == 0)
                {
                    var progress = Volatile.Read(ref _latestProgress);
                    status.Text = FormatProgress(progress);
                    progressBar.IsIndeterminate = progress.Total <= 0;
                    progressBar.Maximum = Math.Max(1, progress.Total);
                    progressBar.Value = Math.Max(0, Math.Min(progress.Completed, progressBar.Maximum));
                }

                if (!window.IsVisible && elapsed.ElapsedMilliseconds >= ShowDelayMilliseconds)
                    window.Show();
            };
            timer.Start();
            _ready.SetResult(true);
            Dispatcher.Run();
        }
        catch (Exception exception)
        {
            if (!_ready.TrySetException(exception))
                Trace.TraceError("CAD Lens operation progress failed: {0}", exception);
        }
        finally
        {
            timer?.Stop();
        }
    }

    private void CancelOnDispatcher(TextBlock status, ProgressBar progressBar, Button cancelButton)
    {
        if (Volatile.Read(ref _disposed) != 0 || Interlocked.Exchange(ref _cancellationRequested, 1) != 0)
            return;

        status.Text = UiText.Current.Get(
            Volatile.Read(ref _latestProgress).Stage == OperationStage.WaitingForSelection
                ? "Cancelling… Press Esc in AutoCAD to finish object selection."
                : "Cancelling…");
        cancelButton.Content = UiText.Current.Get("Cancelling…");
        cancelButton.IsEnabled = false;
        progressBar.IsIndeterminate = true;

        try
        {
            _requestCancellation();
        }
        catch (Exception exception)
        {
            status.Text = UiText.Current.Get("Unable to request cancellation.");
            Trace.TraceError("CAD Lens operation cancellation failed: {0}", exception);
        }
    }

    private void OnWindowClosing(object? sender, CancelEventArgs args)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        args.Cancel = true;
        RequestCancellation();
    }

    private Window CreateWindow(out TextBlock status, out ProgressBar progressBar, out Button cancelButton)
    {
        var window = CreateThemedWindow();
        status = new TextBlock
        {
            Name = "OperationProgressText",
            MinHeight = 38,
            FontSize = 12,
            Text = FormatProgress(new OperationProgress(OperationStage.Preparing)),
            TextWrapping = TextWrapping.Wrap,
            Style = new Style(typeof(TextBlock))
        };
        progressBar = new ProgressBar
        {
            Name = "OperationProgressBar",
            Height = 3,
            Margin = new Thickness(0, 8, 0, 14),
            IsIndeterminate = true,
            BorderThickness = new Thickness(0),
            Style = new Style(typeof(ProgressBar))
        };
        progressBar.SetResourceReference(Control.ForegroundProperty, "QuietAccent");
        progressBar.SetResourceReference(Control.BackgroundProperty, "QuietBorder");
        cancelButton = new Button
        {
            Name = "CancelOperationButton",
            Content = UiText.Current.Get("Cancel"),
            MinWidth = 96,
            HorizontalAlignment = HorizontalAlignment.Right,
            Style = (Style) window.FindResource("QuietButton")
        };
        var content = new StackPanel {Margin = new Thickness(16)};
        content.Children.Add(status);
        content.Children.Add(progressBar);
        content.Children.Add(cancelButton);

        var layout = new StackPanel();
        layout.Children.Add(CreateHeader(window));
        layout.Children.Add(content);
        var frame = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = layout
        };
        frame.SetResourceReference(Border.BackgroundProperty, "QuietBackground");
        frame.SetResourceReference(Border.BorderBrushProperty, "QuietBorder");
        window.Content = frame;

        return window;
    }

    private Window CreateThemedWindow()
    {
        var window = new Window
        {
            Title = "CAD Lens",
            Width = 340,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            Style = new Style(typeof(Window))
        };
        window.Resources.MergedDictionaries.Add(new QuietTheme());

        foreach (var (key, color) in _palette)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            window.Resources[key] = brush;
        }

        window.SetResourceReference(Control.BackgroundProperty, "QuietBackground");
        window.SetResourceReference(Control.ForegroundProperty, "QuietText");
        WindowChrome.SetWindowChrome(
            window,
            new WindowChrome
            {
                CaptionHeight = 50,
                CornerRadius = new CornerRadius(12),
                GlassFrameThickness = new Thickness(0),
                ResizeBorderThickness = new Thickness(0)
            });

        if (_ownerBounds is not { } bounds)
            return window;

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = bounds.Left;
        window.Top = bounds.Top;
        window.Loaded += (_, _) => CenterOverOwner(window, bounds);

        return window;
    }

    private void CenterOverOwner(Window window, Rect bounds)
    {
        var left = bounds.Left + (bounds.Width - window.ActualWidth) / 2;
        var top = bounds.Top + (bounds.Height - window.ActualHeight) / 2;
        window.Left = Math.Min(
            Math.Max(left, _workArea.Left),
            Math.Max(_workArea.Left, _workArea.Right - window.ActualWidth));
        window.Top = Math.Min(
            Math.Max(top, _workArea.Top),
            Math.Max(_workArea.Top, _workArea.Bottom - window.ActualHeight));
    }

    private static Border CreateHeader(Window window)
    {
        var closeButton = new Button
        {
            Name = "OperationProgressCloseButton",
            Content = "×",
            Width = 30,
            Height = 30,
            MinHeight = 30,
            FontSize = 18,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Style = (Style) window.FindResource("QuietButton")
        };
        System.Windows.Automation.AutomationProperties.SetName(closeButton, UiText.Current.Get("Cancel"));
        WindowChrome.SetIsHitTestVisibleInChrome(closeButton, true);
        closeButton.Click += (_, _) => window.Close();
        var headerContent = new Grid {Margin = new Thickness(12, 0, 8, 0)};
        var accent = new Border
        {
            Width = 3,
            Height = 20,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(2)
        };
        accent.SetResourceReference(Border.BackgroundProperty, "QuietAccent");
        headerContent.Children.Add(accent);
        headerContent.Children.Add(
            new TextBlock
            {
                Text = "CAD Lens",
                FontWeight = FontWeights.SemiBold,
                Style = new Style(typeof(TextBlock)),
                Margin = new Thickness(11, 0, 40, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
        headerContent.Children.Add(closeButton);
        var header = new Border {Height = 50, CornerRadius = new CornerRadius(11, 11, 0, 0), Child = headerContent};
        header.SetResourceReference(Border.BackgroundProperty, "QuietHeader");

        return header;
    }

    private static (string Key, Color Color)[] CapturePalette(ResourceDictionary? appearance)
    {
        List<(string Key, Color Color)> colors = [];

        if (appearance is null)
            return [];

        foreach (DictionaryEntry entry in appearance)
        {
            if (entry is {Key: string key, Value: SolidColorBrush brush})
                colors.Add((key, brush.Color));
        }

        return [.. colors];
    }

    private static string StageText(OperationStage stage) => stage switch
    {
        OperationStage.Preparing => "Preparing…",
        OperationStage.Reading => "Reading drawing objects…",
        OperationStage.Building => "Building explorer groups…",
        OperationStage.Selecting => "Selecting drawing objects…",
        OperationStage.Isolating => "Preparing isolation…",
        OperationStage.CreatingGraphics => "Creating temporary graphics…",
        OperationStage.DisplayingGraphics => "Displaying temporary graphics…",
        OperationStage.Regenerating => "Regenerating drawing view…",
        OperationStage.Clearing => "Clearing temporary isolation…",
        OperationStage.Focusing => "Focusing view…",
        OperationStage.Checking => "Checking object availability…",
        OperationStage.WaitingForSelection => "Waiting for object selection…",
        _ => "Working…"
    };
}