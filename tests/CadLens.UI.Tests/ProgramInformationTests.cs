using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>Program information remains accessible without activating a drawing lens.</summary>
[Collection("Language changes")]
public sealed class ProgramInformationTests
{
    /// <summary>The compact About popup localizes live, provides support links, and closes with Escape.</summary>
    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void AboutPopupWorksWithoutAnActiveLens(string theme)
    {
        WpfTest.Run(() =>
        {
            var language = UiText.Current.Preference;
            using var file = new SettingsFile();
            var lens = new CounterLens(new CounterViewModel(new CounterService()));
            using var model = new ExplorerViewModel([lens]);
            var appearance = new AppearancePreferences(file.Service) {Theme = theme};
            var window = new ExplorerWindow(model, appearance);

            try
            {
                UiText.Current.Select(LanguagePreference.English, persist: false);
                window.Show();
                var button = Assert.IsType<ToggleButton>(window.FindName("AboutButton"));
                var popup = Assert.IsType<Popup>(window.FindName("AboutPopup"));
                button.IsChecked = true;
                Pump(window);
                Assert.True(popup.IsOpen);
                Assert.False(model.IsLensActive);
                Assert.Equal(0, lens.ActivationCount);
                Assert.Equal("About CAD Lens", AutomationProperties.GetName(button));
                var panel = Assert.IsType<Border>(popup.Child);
                Assert.Contains("About CAD Lens", Texts(panel));
                Assert.Contains("Questions and suggestions", Runs(panel));
                Assert.Equal(
                    new Uri("https://github.com/vildar82/CadLens"),
                    Assert.IsType<Hyperlink>(window.FindName("ProjectLink")).NavigateUri);
                Assert.Equal(
                    new Uri("https://github.com/vildar82/CadLens/issues"),
                    Assert.IsType<Hyperlink>(window.FindName("IssuesLink")).NavigateUri);

                UiText.Current.Select(LanguagePreference.Russian, persist: false);
                Pump(window);
                Assert.Contains("О CAD Lens", Texts(panel));
                Assert.Contains("Вопросы и предложения", Runs(panel));
                Assert.Equal("О CAD Lens", AutomationProperties.GetName(button));
                Assert.True(popup.IsOpen);
                Assert.False(model.IsLensActive);
                Render(panel, $"program-information-{theme}.png");
                var source = Assert.IsAssignableFrom<PresentationSource>(PresentationSource.FromVisual(panel));
                panel.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Escape)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent
                });
                Pump(window);
                Assert.False(popup.IsOpen);
                Assert.False(button.IsChecked);
                Assert.Equal(0, lens.ActivationCount);
            }
            finally
            {
                window.Close();
                UiText.Current.Select(language, persist: false);
            }
        });
    }

    private static IEnumerable<string> Texts(DependencyObject panel) =>
        WpfTest.Descendants(panel).OfType<TextBlock>().Select(block => block.Text);

    private static IEnumerable<string> Runs(DependencyObject panel) =>
        WpfTest.Descendants(panel).OfType<TextBlock>()
            .SelectMany(block => block.Inlines.OfType<Hyperlink>())
            .SelectMany(link => link.Inlines.OfType<Run>())
            .Select(run => run.Text);

    private static void Pump(FrameworkElement element) =>
        element.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void Render(FrameworkElement element, string name)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int) element.ActualWidth, (int) element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = System.IO.File.Create(System.IO.Path.Combine(AppContext.BaseDirectory, name));
        encoder.Save(output);
    }
}
