using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Input;
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

    /// <summary>Raised by the hidden drawing diagnostics shortcut.</summary>
    public event EventHandler? DiagnosticsRequested;

    /// <summary>Creates the panel without changing application-wide WPF resources.</summary>
    /// <param name="viewModel">Constructor-injected explorer commands.</param>
    public ExplorerWindow(ExplorerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        UiText.Current.PropertyChanged += OnLanguageChanged;
        ApplyLocalizedAppearanceLabels();
        viewModel.PropertyChanged += OnViewModelChanged;
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
        PreviewKeyDown += OnPreviewKeyDown;
        UpdateMode();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.F12 || Keyboard.Modifiers != (ModifierKeys.Control | ModifierKeys.Shift))
            return;

        args.Handled = true;
        DiagnosticsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ExplorerViewModel.IsLensActive))
            UpdateMode();

        if (args.PropertyName == nameof(ExplorerViewModel.ActiveView))
            ApplyLocalizedAppearanceLabels();
    }

    private void OnSourceInitialized(object? sender, EventArgs args) => UpdateMode();

    private void OnClosed(object? sender, EventArgs args)
    {
        _viewModel.PropertyChanged -= OnViewModelChanged;
        UiText.Current.PropertyChanged -= OnLanguageChanged;
    }

    private void LanguageClicked(object sender, RoutedEventArgs args)
    {
        var button = (Button)sender;
        button.ContextMenu.Resources = Resources;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = PlacementMode.Bottom;
        button.ContextMenu.IsOpen = true;
    }

    private void LanguageSelected(object sender, RoutedEventArgs args)
    {
        if (Enum.TryParse<LanguagePreference>(((MenuItem)sender).Tag as string, out var preference))
            UiText.Current.Select(preference);
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs args) => ApplyLocalizedAppearanceLabels();

    private void ApplyLocalizedAppearanceLabels()
    {
        // Local resources override optional appearance-menu defaults when both features are installed.
        var viewResources = _viewModel.ActiveView?.Resources;

        foreach (var (key, english) in AppearanceLabels)
        {
            var translated = UiText.Current.Get(english);
            Resources[key] = translated;

            if (viewResources is not null)
                viewResources[key] = translated;
        }
    }

    private static readonly (string Key, string English)[] AppearanceLabels =
    [
        ("AppearanceLabel", "Appearance"),
        ("AppearanceHelp", "Choose a theme, palette, and accent color."),
        ("ThemeLabel", "Theme"),
        ("FollowAutoCadLabel", "Follow AutoCAD"),
        ("LightLabel", "Light"),
        ("DarkLabel", "Dark"),
        ("PaletteLabel", "Palette"),
        ("QuietLabel", "Quiet"),
        ("GraphiteLabel", "Graphite"),
        ("PaperLabel", "Paper"),
        ("AccentLabel", "Accent"),
        ("MintLabel", "Mint"),
        ("BlueLabel", "Blue"),
        ("VioletLabel", "Violet"),
        ("AmberLabel", "Amber"),
        ("RestoreDefaultsLabel", "Restore defaults"),
        ("AppearanceChangesApplied", "Changes apply immediately."),
        ("EditCutLabel", "Cut"),
        ("EditCopyLabel", "Copy"),
        ("EditPasteLabel", "Paste"),
        ("EditSelectAllLabel", "Select all"),
        ("AppearanceSaveFailed", "Unable to save preferences. Changes apply for this session.")
    ];

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
            Width = 340;
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

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

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
