using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using CadLens.Lenses;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>Blank header space remains draggable while header controls stay interactive.</summary>
[Collection("Language changes")]
public sealed class WindowDraggingTests
{
    private const int ClientArea = 1;
    private const int CaptionArea = 2;
    private const uint NonClientHitTest = 0x0084;

    /// <summary>The lens viewport routes its empty space to the caption and its buttons to the client.</summary>
    [Fact]
    public void EmptyLensViewportIsDraggableWithoutDisablingHeaderButtons()
    {
        WpfTest.Run(() =>
        {
            var previousLanguage = UiText.Current.Preference;
            using var file = new SettingsFile();
            using var layers = new ObjectExplorerLens(new AppearanceTests.Actions(), DrawingGrouping.Layers);
            using var objects = new ObjectExplorerLens(new AppearanceTests.Actions(), DrawingGrouping.ObjectTypes);
            using var model = new ExplorerViewModel([layers, objects]);
            var window = new ExplorerWindow(model, settings: file.Service) {ShowActivated = false};

            try
            {
                UiText.Current.Select(LanguagePreference.English, persist: false);
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var viewport = WpfTest.Descendants(window).OfType<ScrollViewer>().Single();
                var buttons = WpfTest.Descendants(viewport).OfType<ToggleButton>().ToArray();
                Assert.Equal(2, buttons.Length);
                var lastButton = buttons[^1];
                var buttonRight = lastButton.TranslatePoint(new Point(lastButton.ActualWidth, lastButton.ActualHeight / 2), window);
                var viewportRight = viewport.TranslatePoint(new Point(viewport.ActualWidth, 0), window);
                Assert.True(viewportRight.X - buttonRight.X > 4, "The compact header must have empty lens viewport space.");
                var emptySpace = new Point((buttonRight.X + viewportRight.X) / 2, buttonRight.Y);

                Assert.Equal(CaptionArea, HitTest(window, emptySpace));

                foreach (var button in buttons)
                    Assert.Equal(ClientArea, HitTest(window, button.TranslatePoint(new Point(button.ActualWidth / 2, button.ActualHeight / 2), window)));

                var about = Assert.IsType<ToggleButton>(window.FindName("AboutButton"));
                Assert.Equal(ClientArea, HitTest(window, about.TranslatePoint(new Point(about.ActualWidth / 2, about.ActualHeight / 2), window)));
            }
            finally
            {
                window.Close();
                UiText.Current.Select(previousLanguage, persist: false);
            }
        });
    }

    /// <summary>Overflow navigation stays clickable when more lenses are registered than the header can fit.</summary>
    [Fact]
    public void OverflowScrollbarRemainsInTheClientArea()
    {
        WpfTest.Run(() =>
        {
            var previousLanguage = UiText.Current.Preference;
            using var file = new SettingsFile();
            using var layers = new ObjectExplorerLens(new AppearanceTests.Actions(), DrawingGrouping.Layers);
            using var objects = new ObjectExplorerLens(new AppearanceTests.Actions(), DrawingGrouping.ObjectTypes);
            var counter = new CounterLens(new CounterViewModel(new CounterService()));
            using var model = new ExplorerViewModel([layers, objects, counter]);
            var window = new ExplorerWindow(model, settings: file.Service) {ShowActivated = false};

            try
            {
                UiText.Current.Select(LanguagePreference.English, persist: false);
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var viewport = WpfTest.Descendants(window).OfType<ScrollViewer>().Single();
                Assert.True(viewport.ExtentWidth > viewport.ViewportWidth);
                var scrollbar = WpfTest.Descendants(viewport).OfType<ScrollBar>()
                    .Single(candidate => candidate.Orientation == Orientation.Horizontal && candidate.IsVisible);
                Assert.Equal(ClientArea, HitTest(window, scrollbar.TranslatePoint(new Point(scrollbar.ActualWidth / 2, scrollbar.ActualHeight / 2), window)));
            }
            finally
            {
                window.Close();
                UiText.Current.Select(previousLanguage, persist: false);
            }
        });
    }

    private static nint HitTest(Window window, Point point)
    {
        var screen = window.PointToScreen(point);
        var coordinates = ((int) screen.Y & 0xffff) << 16 | ((int) screen.X & 0xffff);
        return SendMessage(new WindowInteropHelper(window).Handle, NonClientHitTest, nint.Zero, coordinates);
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nint parameter, nint coordinates);
}
