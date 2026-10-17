using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
    public OperationProgressWindow(Action requestCancellation)
    {
        _requestCancellation = requestCancellation;
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

    private static Window CreateWindow(out TextBlock status, out ProgressBar progressBar, out Button cancelButton)
    {
        status = new TextBlock
        {
            Name = "OperationProgressText",
            MinHeight = 38,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Text = FormatProgress(new OperationProgress(OperationStage.Preparing)),
            TextWrapping = TextWrapping.Wrap,
            Style = new Style(typeof(TextBlock))
        };
        progressBar = new ProgressBar
        {
            Name = "OperationProgressBar",
            Height = 5,
            Margin = new Thickness(0, 10, 0, 14),
            IsIndeterminate = true,
            Foreground = new SolidColorBrush(Color.FromRgb(45, 111, 177)),
            Background = new SolidColorBrush(Color.FromRgb(224, 229, 234)),
            BorderThickness = new Thickness(0),
            Style = new Style(typeof(ProgressBar))
        };
        cancelButton = new Button
        {
            Name = "CancelOperationButton",
            Content = UiText.Current.Get("Cancel"),
            MinWidth = 100,
            MinHeight = 28,
            Padding = new Thickness(12, 4, 12, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
            Style = new Style(typeof(Button))
        };
        var content = new StackPanel {Margin = new Thickness(16)};
        content.Children.Add(status);
        content.Children.Add(progressBar);
        content.Children.Add(cancelButton);

        return new Window
        {
            Title = "CAD Lens",
            Width = 380,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(250, 250, 250)),
            Foreground = new SolidColorBrush(Color.FromRgb(32, 36, 40)),
            Style = new Style(typeof(Window)),
            Content = content
        };
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