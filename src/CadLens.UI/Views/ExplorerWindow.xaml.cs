using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using JetBrains.Annotations;

namespace CadLens.UI;

/// <summary>The locally themed modeless drawing explorer.</summary>
public partial class ExplorerWindow
{
    private const double CompactHeight = 52;
    private const double ExpandedMinimumHeight = 450;
    private const double MinimumPanelWidth = 300;
    private const uint NearestMonitor = 2;
    private readonly ExplorerViewModel _viewModel;
    private Size _expandedSize = new(370, 660);

    /// <summary>Creates the panel without changing application-wide WPF resources.</summary>
    /// <param name="viewModel">Constructor-injected explorer commands.</param>
    public ExplorerWindow(ExplorerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelChanged;
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
        UpdateMode();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ExplorerViewModel.IsLensActive))
            UpdateMode();
    }

    private void OnSourceInitialized(object? sender, EventArgs args) => UpdateMode();

    private void OnClosed(object? sender, EventArgs args) => _viewModel.PropertyChanged -= OnViewModelChanged;

    private void UpdateMode()
    {
        if (_viewModel.IsLensActive)
        {
            var workArea = GetMonitorWorkArea();
            MaxWidth = workArea.Width;
            MaxHeight = workArea.Height;
            MinWidth = Math.Min(MinimumPanelWidth, workArea.Width);
            MinHeight = Math.Min(ExpandedMinimumHeight, workArea.Height);
            Width = Math.Clamp(_expandedSize.Width, MinWidth, MaxWidth);
            Height = Math.Clamp(_expandedSize.Height, MinHeight, MaxHeight);
            ResizeMode = ResizeMode.CanResizeWithGrip;

            if (!double.IsNaN(Left))
                Left = Math.Clamp(Left, workArea.Left, workArea.Right - Width);

            if (!double.IsNaN(Top))
                Top = Math.Clamp(Top, workArea.Top, workArea.Bottom - Height);
        }
        else
        {
            if (ResizeMode != ResizeMode.NoResize)
                _expandedSize = new Size(Width, Height);

            MinHeight = CompactHeight;
            Height = CompactHeight;
            Width = MinimumPanelWidth;
            ResizeMode = ResizeMode.NoResize;
        }
    }

    private Rect GetMonitorWorkArea()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };

        if (handle == IntPtr.Zero || !GetMonitorInfo(MonitorFromWindow(handle, NearestMonitor), ref info))
            return SystemParameters.WorkArea;

        var transform = HwndSource.FromHwnd(handle)?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var topLeft = transform.Transform(new Point(info.Work.Left, info.Work.Top));
        var bottomRight = transform.Transform(new Point(info.Work.Right, info.Work.Bottom));

        return new Rect(topLeft, bottomRight);
    }

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();

    private void ShowStatus(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ToolTip: ToolTip tooltip } button)
        {
            tooltip.PlacementTarget = button;
            tooltip.StaysOpen = false;
            tooltip.IsOpen = true;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }
}