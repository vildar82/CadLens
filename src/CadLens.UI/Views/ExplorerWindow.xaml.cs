using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
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
    private bool _hostIsLight;
    private Size _expandedSize = new(370, 660);

    /// <summary>Raised by the hidden drawing diagnostics shortcut.</summary>
    public event EventHandler? DiagnosticsRequested;

    /// <summary>Creates the panel without changing application-wide WPF resources.</summary>
    /// <param name="viewModel">Constructor-injected explorer commands.</param>
    /// <param name="appearance">Optional isolated preferences for managed tests.</param>
    public ExplorerWindow(ExplorerViewModel viewModel, AppearancePreferences? appearance = null)
    {
        Appearance = appearance ?? new AppearancePreferences();
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        UiText.Current.PropertyChanged += OnLanguageChanged;
        ApplyLocalizedAppearanceLabels();
        viewModel.PropertyChanged += OnLocalizedViewChanged;
        Closed += OnLanguageWindowClosed;
        viewModel.PropertyChanged += OnViewModelChanged;
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
        PreviewKeyDown += OnPreviewKeyDown;
        Appearance.PropertyChanged += OnAppearanceChanged;
        UpdateMode();
        ApplyAppearance();
    }

    /// <summary>Appearance preferences for this panel.</summary>
    public AppearancePreferences Appearance { get; }

    /// <summary>Updates the host theme without changing explicit Light or Dark preferences.</summary>
    /// <param name="isLight">Whether AutoCAD currently uses its light interface.</param>
    public void SetHostTheme(bool isLight)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SetHostTheme(isLight));
            return;
        }

        _hostIsLight = isLight;
        ApplyAppearance();
    }

    private void OnAppearanceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(AppearancePreferences.Theme) or nameof(AppearancePreferences.Palette)
            or nameof(AppearancePreferences.Accent))
            ApplyAppearance();
    }

    private void ApplyAppearance()
    {
        AppearancePalette.Apply(Resources, Appearance.Theme, Appearance.Palette, Appearance.Accent, _hostIsLight);

        if (_viewModel.ActiveView is { } view)
            AppearancePalette.Apply(
                view.Resources,
                Appearance.Theme,
                Appearance.Palette,
                Appearance.Accent,
                _hostIsLight);
    }

    private void RestoreAppearanceClicked(object sender, RoutedEventArgs args) => Appearance.Reset();

    private void OnPreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Escape && AppearancePopup.IsOpen)
        {
            AppearancePopup.IsOpen = false;
            AppearanceButton.Focus();
            args.Handled = true;
            return;
        }

        if (args.Key != Key.F12 || Keyboard.Modifiers != (ModifierKeys.Control | ModifierKeys.Shift))
            return;

        args.Handled = true;
        DiagnosticsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        switch (args.PropertyName)
        {
            case nameof(ExplorerViewModel.IsLensActive):
                UpdateMode();
                break;
            case nameof(ExplorerViewModel.ActiveView):
                ApplyAppearance();
                break;
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs args) => UpdateMode();

    private void OnClosed(object? sender, EventArgs args)
    {
        AppearancePopup.IsOpen = false;
        Appearance.PropertyChanged -= OnAppearanceChanged;
        _viewModel.PropertyChanged -= OnViewModelChanged;
    }

    private void OnLanguageWindowClosed(object? sender, EventArgs args)
    {
        _viewModel.PropertyChanged -= OnLocalizedViewChanged;
        UiText.Current.PropertyChanged -= OnLanguageChanged;
        Closed -= OnLanguageWindowClosed;
    }

    private void OnLocalizedViewChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ExplorerViewModel.ActiveView))
            ApplyLocalizedAppearanceLabels();
    }

    private void LanguageClicked(object sender, RoutedEventArgs args)
    {
        var button = (Button) sender;
        var menu = button.ContextMenu;

        if (menu is null)
            return;

        menu.Resources = Resources;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void LanguageSelected(object sender, RoutedEventArgs args)
    {
        if (Enum.TryParse<LanguagePreference>(((MenuItem) sender).Tag as string, out var preference))
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

            viewResources?[key] = translated;
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
        var info = new MonitorInfo {Size = Marshal.SizeOf<MonitorInfo>()};

        if (handle == IntPtr.Zero || !GetMonitorInfo(MonitorFromWindow(handle, NearestMonitor), ref info))
            return SystemParameters.WorkArea;

        var transform = HwndSource.FromHwnd(handle)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
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
