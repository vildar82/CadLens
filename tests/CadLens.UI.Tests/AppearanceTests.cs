using System.IO;
using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CadLens.Lenses;
using Common;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>Appearance persistence and actual WPF popup/resource behavior.</summary>
[Collection("WPF")]
public sealed class AppearanceTests
{
    private static readonly string[] ThemeChoices = ["Light", "Dark"];
    private static readonly string[] ForegroundKeys = ["QuietText", "QuietMuted", "QuietAccent"];

    private static readonly string[] BackgroundKeys =
        ["QuietBackground", "QuietSurface", "QuietHover", "QuietSelected"];

    /// <summary>Choices survive reopen and reset saves the original defaults.</summary>
    [Fact]
    public void PreferencesRoundTripAndReset()
    {
        using var file = new SettingsFile();
        var preferences = new AppearancePreferences(file.Service)
            {Theme = "Light", Palette = "Paper", Accent = "Amber"};
        var reopened = new AppearancePreferences(file.Service);
        Assert.Equal(("Light", "Paper", "Amber"), (reopened.Theme, reopened.Palette, reopened.Accent));
        Assert.False(preferences.SaveFailed);
        reopened.Reset();
        var defaults = new AppearancePreferences(file.Service);
        Assert.Equal(("Follow AutoCAD", "Quiet", "Mint"), (defaults.Theme, defaults.Palette, defaults.Accent));
        Assert.Empty(Directory.GetFiles(file.Directory, "*.tmp"));
    }

    /// <summary>Invalid files and unsupported values cannot prevent opening the panel.</summary>
    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{\"Theme\":\"Solarized\",\"Palette\":null,\"Accent\":99}")]
    [InlineData("{\"Theme\":\"Unsupported\",\"Palette\":\"Unsupported\",\"Accent\":\"Unsupported\"}")]
    public void InvalidSettingsUseDefaults(string json)
    {
        using var file = new SettingsFile();
        File.WriteAllText(file.Path, json);
        var preferences = new AppearancePreferences(file.Service);
        Assert.Equal(
            ("Follow AutoCAD", "Quiet", "Mint"),
            (preferences.Theme, preferences.Palette, preferences.Accent));
    }

    /// <summary>A blocked preference file leaves session changes usable and reports the save failure.</summary>
    [Fact]
    public void FailedSaveDoesNotRejectSessionChoice()
    {
        using var file = new SettingsFile();
        Directory.CreateDirectory(file.Path);
        var preferences = new AppearancePreferences(file.Service) {Theme = "Light"};
        Assert.Equal("Light", preferences.Theme);
        Assert.True(preferences.SaveFailed);
        Assert.Empty(Directory.GetFiles(file.Directory, "*.tmp"));
    }

    /// <summary>Following AutoCAD updates locally; explicit themes override later host changes.</summary>
    [Fact]
    public void HostChangesAndWindowResourcesStayLocal()
    {
        WpfTest.Run(() =>
        {
            using var firstFile = new SettingsFile();
            using var secondFile = new SettingsFile();
            using var firstModel = new ExplorerViewModel(
                [new CounterLens(new CounterViewModel(new CounterService()))]);
            using var secondModel =
                new ExplorerViewModel([new CounterLens(new CounterViewModel(new CounterService()))]);
            var first = new ExplorerWindow(firstModel, new AppearancePreferences(firstFile.Service));
            var second = new ExplorerWindow(secondModel, new AppearancePreferences(secondFile.Service));
            var defaultBackground = Brush(first, "QuietBackground").Color;
            first.SetHostTheme(true);
            Assert.NotEqual(defaultBackground, Brush(first, "QuietBackground").Color);
            Assert.Equal(defaultBackground, Brush(second, "QuietBackground").Color);
            first.Appearance.Theme = "Dark";
            first.SetHostTheme(true);
            Assert.Equal(defaultBackground, Brush(first, "QuietBackground").Color);
            Assert.Null(Application.Current);
            first.Close();
            second.Close();
        });
    }

    /// <summary>Both production views repaint without rereading the drawing or running drawing actions.</summary>
    [Fact]
    public void AppearanceRepaintsBothDrawingLensesWithoutNativeActions()
    {
        WpfTest.Run(() =>
        {
            using var file = new SettingsFile();
            var actions = new Actions();
            using var layers = new ObjectExplorerLens(actions, DrawingGrouping.Layers);
            using var types = new ObjectExplorerLens(actions, DrawingGrouping.ObjectTypes);
            using var model = new ExplorerViewModel([layers, types]);
            var window = new ExplorerWindow(model, new AppearancePreferences(file.Service));
            RenderSized((FrameworkElement) window.Content, 300, 52, "appearance-compact-dark.png");
            window.Appearance.Theme = "Light";
            RenderSized((FrameworkElement) window.Content, 300, 52, "appearance-compact-light.png");
            model.ToggleLensCommand.ExecuteAsync(model.Lenses[0]).GetAwaiter().GetResult();
            window.Appearance.Theme = "Light";
            window.Appearance.Palette = "Paper";
            window.Appearance.Accent = "Amber";
            Assert.Equal(Brush(window, "QuietBackground").Color, Brush(layers.View, "QuietBackground").Color);
            Assert.Equal(1, actions.ReadCount);
            Assert.Equal(0, actions.DrawingActionCount);
            model.ToggleLensCommand.ExecuteAsync(model.Lenses[1]).GetAwaiter().GetResult();
            Assert.Equal(Brush(window, "QuietBackground").Color, Brush(types.View, "QuietBackground").Color);
            window.Appearance.Theme = "Dark";
            Assert.Equal(Brush(window, "QuietBackground").Color, Brush(types.View, "QuietBackground").Color);
            Assert.Equal(2, actions.ReadCount);
            RenderSized((FrameworkElement) window.Content, 300, 450, "appearance-lens-dark-300x450.png");
            window.Appearance.Theme = "Light";
            RenderSized((FrameworkElement) window.Content, 370, 660, "appearance-lens-light-370x660.png");
            window.Close();
        });
    }

    /// <summary>Small labels remain readable on normal, hovered, and selected surfaces for every preset.</summary>
    [Fact]
    public void AllPalettesKeepReadableTextContrast()
    {
        WpfTest.Run(() =>
        {
            using var file = new SettingsFile();
            using var model = new ExplorerViewModel([new CounterLens(new CounterViewModel(new CounterService()))]);
            var window = new ExplorerWindow(model, new AppearancePreferences(file.Service));

            foreach (var theme in ThemeChoices)
            foreach (var palette in window.Appearance.Palettes)
            foreach (var accent in window.Appearance.Accents)
            {
                window.Appearance.Theme = theme;
                window.Appearance.Palette = palette;
                window.Appearance.Accent = accent;

                foreach (var foreground in ForegroundKeys)
                foreach (var background in BackgroundKeys)
                {
                    var first = Luminance(Brush(window, foreground).Color);
                    var second = Luminance(Brush(window, background).Color);
                    var contrast = (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
                    Assert.True(
                        contrast >= 4.5,
                        $"{theme}/{palette}/{accent}: {foreground} on {background} has {contrast:F2}:1 contrast.");
                }
            }

            window.Close();
        });
    }

    /// <summary>Real popup and dropdown choices stay bound and repaint while open.</summary>
    [Fact]
    public void OpenPopupAndToolTipFollowLiveTheme()
    {
        WpfTest.Run(() =>
        {
            using var file = new SettingsFile();
            using var model = new ExplorerViewModel([new CounterLens(new CounterViewModel(new CounterService()))]);
            var window = new ExplorerWindow(model, new AppearancePreferences(file.Service));
            var popup = Assert.IsType<Popup>(window.FindName("AppearancePopup"));
            var choice = Assert.IsType<ComboBox>(window.FindName("ThemeChoice"));
            var button = Assert.IsType<ToggleButton>(window.FindName("AppearanceButton"));
            window.Show();
            try
            {
                popup.IsOpen = true;
                Pump(window);
                Assert.Same(window.Appearance, choice.DataContext);
                Assert.Equal("Follow AutoCAD", choice.SelectedValue);
                var panel = (Border) popup.Child;
                var darkBackground = ((SolidColorBrush) panel.Background).Color;
                choice.SelectedValue = "Light";
                Pump(window);
                Assert.Equal("Light", window.Appearance.Theme);
                Assert.NotEqual(darkBackground, ((SolidColorBrush) panel.Background).Color);
                Render(panel, "appearance-popup-light.png");
                choice.IsDropDownOpen = true;
                Pump(window);
                var dropDown = (Popup) choice.Template.FindName("PART_Popup", choice);
                Assert.True(dropDown.IsOpen);
                Assert.Equal(
                    Brush(window, "QuietSurface").Color,
                    ((SolidColorBrush) ((Border) dropDown.Child).Background).Color);
                Render((FrameworkElement) dropDown.Child, "appearance-dropdown-light.png");
                window.Appearance.Theme = "Dark";
                Pump(window);
                Assert.Equal(
                    Brush(window, "QuietSurface").Color,
                    ((SolidColorBrush) ((Border) dropDown.Child).Background).Color);
                Render((FrameworkElement) dropDown.Child, "appearance-dropdown-dark.png");
                choice.IsDropDownOpen = false;
                Render(panel, "appearance-popup-dark.png");
                popup.IsOpen = false;
                var menu = new ContextMenu {PlacementTarget = button, Resources = window.Resources};
                var checkedChoice = new MenuItem {Header = "Selected language", IsCheckable = true, IsChecked = true};
                menu.Items.Add(checkedChoice);
                menu.IsOpen = true;
                Pump(window);
                Assert.Equal(Brush(window, "QuietSurface").Color, ((SolidColorBrush) menu.Background).Color);
                Assert.Equal(
                    Visibility.Visible,
                    ((TextBlock) checkedChoice.Template.FindName("Checkmark", checkedChoice)).Visibility);
                window.Appearance.Theme = "Light";
                Pump(window);
                Assert.Equal(Brush(window, "QuietSurface").Color, ((SolidColorBrush) menu.Background).Color);
                Assert.Equal(Brush(window, "QuietText").Color, ((SolidColorBrush) checkedChoice.Foreground).Color);
                Render(menu, "appearance-context-menu-light.png");
                menu.IsOpen = false;
                window.Appearance.Theme = "Dark";
                var tooltip = new ToolTip
                {
                    Content = button.ToolTip,
                    PlacementTarget = button,
                    Style = (Style) button.FindResource(typeof(ToolTip))
                };
                button.ToolTip = tooltip;
                tooltip.IsOpen = true;
                Pump(window);
                Assert.Equal(Brush(window, "QuietSurface").Color, ((SolidColorBrush) tooltip.Background).Color);
                Assert.Equal(Brush(window, "QuietText").Color, ((SolidColorBrush) tooltip.Foreground).Color);
                window.Appearance.Theme = "Light";
                Pump(window);
                Assert.Equal(Brush(window, "QuietSurface").Color, ((SolidColorBrush) tooltip.Background).Color);
                Assert.Equal(Brush(window, "QuietText").Color, ((SolidColorBrush) tooltip.Foreground).Color);
                Render(tooltip, "appearance-tooltip-light.png");
                tooltip.IsOpen = false;
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>Large lists keep a usable thumb and matching drag travel in both drawing lenses.</summary>
    [Theory]
    [InlineData(DrawingGrouping.Layers, "Dark")]
    [InlineData(DrawingGrouping.Layers, "Light")]
    [InlineData(DrawingGrouping.ObjectTypes, "Dark")]
    [InlineData(DrawingGrouping.ObjectTypes, "Light")]
    public void LargeListsKeepUsableScrollThumbs(DrawingGrouping grouping, string theme)
    {
        WpfTest.Run(() =>
        {
            using var file = new SettingsFile();
            var actions = new Actions(114);
            using var lens = new ObjectExplorerLens(actions, grouping);
            using var model = new ExplorerViewModel([lens]);
            var preferences = new AppearancePreferences(file.Service) {Theme = theme};
            var window = new ExplorerWindow(model, preferences);

            try
            {
                model.ToggleLensCommand.ExecuteAsync(model.Lenses[0]).GetAwaiter().GetResult();
                var content = (FrameworkElement) window.Content;
                RenderSized(content, 370, 660, $"appearance-scroll-{grouping}-{theme}.png");
                var list = WpfTest.Descendants(content).OfType<ListBox>().Single();
                var scroller = WpfTest.Descendants(list).OfType<ScrollViewer>().Single();
                var scrollbar = Assert.IsType<ScrollBar>(
                    scroller.Template.FindName("PART_VerticalScrollBar", scroller));
                var track = Assert.IsType<Track>(scrollbar.Template.FindName("PART_Track", scrollbar));
                var thumb = track.Thumb;
                Assert.True(scroller.ScrollableHeight > 0);
                Assert.Equal(8, scrollbar.ActualWidth);
                Assert.True(thumb.ActualHeight >= 24, $"Scrollbar thumb is only {thumb.ActualHeight} px high.");
                Assert.Equal(
                    scrollbar.Maximum - scrollbar.Minimum,
                    Math.Abs(track.ValueFromDistance(0, track.ActualHeight - thumb.ActualHeight)),
                    precision: 6);
                var start = thumb.TranslatePoint(new Point(), scrollbar).Y;
                scroller.ScrollToBottom();
                content.UpdateLayout();
                Pump(content);
                Assert.Equal(scroller.ScrollableHeight, scroller.VerticalOffset);
                Assert.True(thumb.TranslatePoint(new Point(), scrollbar).Y > start);
                Assert.Equal(1, actions.ReadCount);
                Assert.Equal(0, actions.DrawingActionCount);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static SolidColorBrush Brush(FrameworkElement scope, string key) =>
        (SolidColorBrush) scope.FindResource(key);

    private static void Pump(FrameworkElement element) =>
        element.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void RenderSized(FrameworkElement element, int width, int height, string name)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        Render(element, name);
    }

    private static double Luminance(Color color) =>
        0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);

    private static double Linear(byte channel)
    {
        var value = channel / 255d;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static void Render(FrameworkElement element, string name)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(
            (int) Math.Ceiling(element.ActualWidth),
            (int) Math.Ceiling(element.ActualHeight),
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(AppContext.BaseDirectory, name));
        encoder.Save(output);
    }

    internal sealed class Actions(int groupCount = 12) : IObjectExplorerActions
    {
        internal int ReadCount { get; private set; }
        internal int DrawingActionCount { get; private set; }

        public HostResult<ImmutableArray<IPlacedObjectId>> CaptureSelectedObjects() =>
            new HostResult<ImmutableArray<IPlacedObjectId>>.Success([]);

        public void ClearImmediately(bool hostTerminating)
        {
        }

        public Task<HostResult<LensPresentation>> ReadAsync(
            DrawingGrouping grouping,
            IReadOnlySet<string> enabledFilters,
            ImmutableArray<IPlacedObjectId>? selectedObjects,
            CancellationToken cancellationToken)
        {
            ReadCount++;
            ImmutableArray<LensNode> nodes =
            [
                .. Enumerable.Range(1, groupCount).Select(index => new LensNode(
                    index.ToString(),
                    $"Drawing group {index}",
                    [new TestEntityId(index)],
                    [],
                    [],
                    [LensAction.Focus]))
            ];
            return Task.FromResult<HostResult<LensPresentation>>(
                new HostResult<LensPresentation>.Success(
                    new LensPresentation(
                        "Drawing objects",
                        "Model space",
                        nodes,
                        [],
                        "No objects.",
                        "Drawing",
                        "Search drawing",
                        "objects")));
        }

        public Task<HostResult<bool>> SelectAsync(
            ImmutableArray<IPlacedObjectId> objects,
            CancellationToken cancellationToken)
        {
            DrawingActionCount++;
            return Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(true));
        }

        public Task<HostResult<bool>> ClearIsolationAsync(CancellationToken cancellationToken) =>
            ClearAsync(cancellationToken);

        public Task<HostResult<bool>> ClearAsync(CancellationToken cancellationToken) =>
            Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(true));

        public Task<string> IsolateObjectsAsync(
            ImmutableArray<IPlacedObjectId> objects,
            CancellationToken cancellationToken)
        {
            DrawingActionCount++;
            return Task.FromResult("Isolated.");
        }

        public Task<string> FocusAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken)
        {
            DrawingActionCount++;
            return Task.FromResult("Focused.");
        }
    }
}
