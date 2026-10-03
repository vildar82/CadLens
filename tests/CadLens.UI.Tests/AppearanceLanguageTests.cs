using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CadLens.Lenses;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>Checks live language and appearance changes across the separate WPF popup trees.</summary>
[Collection("Language changes")]
public sealed class AppearanceLanguageTests
{
    /// <summary>Open preferences and language/edit menus use the selected language and light palette.</summary>
    [Fact]
    public void OpenPreferencesAndMenusCombineLanguageAndTheme()
    {
        WpfTest.Run(() =>
        {
            var previousLanguage = UiText.Current.Preference;
            using var file = new AppearanceTests.SettingsFile();
            using var layers = new ObjectExplorerLens(new AppearanceTests.Actions(), DrawingGrouping.Layers);
            using var objects = new ObjectExplorerLens(new AppearanceTests.Actions(), DrawingGrouping.ObjectTypes);
            using var model = new ExplorerViewModel([layers, objects]);
            var preferences = new AppearancePreferences(file.Path) {Theme = "Light"};
            var window = new ExplorerWindow(model, preferences);

            try
            {
                UiText.Current.Select(LanguagePreference.English, persist: false);
                model.ToggleLensCommand.ExecuteAsync(model.Lenses[0]).GetAwaiter().GetResult();
                window.Show();
                var appearance = Assert.IsType<Popup>(window.FindName("AppearancePopup"));
                var theme = Assert.IsType<ComboBox>(window.FindName("ThemeChoice"));
                var palette = Assert.IsType<ComboBox>(window.FindName("PaletteChoice"));
                appearance.IsOpen = true;
                Pump(window);
                AssertAppearance(
                    appearance,
                    theme,
                    palette,
                    "Appearance",
                    "Theme",
                    ["Follow AutoCAD", "Light", "Dark"],
                    ["Quiet", "Graphite", "Paper"]);
                UiText.Current.Select(LanguagePreference.Russian, persist: false);
                Pump(window);
                AssertAppearance(
                    appearance,
                    theme,
                    palette,
                    "Оформление",
                    "Тема",
                    ["Как в AutoCAD", "Светлая", "Тёмная"],
                    ["Спокойная", "Графит", "Бумага"]);
                Assert.Equal("Light", theme.SelectedValue);
                Render((FrameworkElement) appearance.Child, "combined-russian-light-appearance.png");
                UiText.Current.Select(LanguagePreference.English, persist: false);
                Pump(window);
                AssertAppearance(
                    appearance,
                    theme,
                    palette,
                    "Appearance",
                    "Theme",
                    ["Follow AutoCAD", "Light", "Dark"],
                    ["Quiet", "Graphite", "Paper"]);
                appearance.IsOpen = false;

                UiText.Current.Select(LanguagePreference.Russian, persist: false);
                Pump(window);
                Render((FrameworkElement) window.Content, "combined-russian-light-root.png");
                var language = Assert.IsType<Button>(window.FindName("LanguageButton"));
                language.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Pump(window);
                var languageMenu = Assert.IsType<ContextMenu>(language.ContextMenu);
                Assert.True(languageMenu.IsOpen);
                Assert.Equal("Как в Windows", Assert.IsType<MenuItem>(languageMenu.Items[0]).Header);
                Assert.True(Assert.IsType<MenuItem>(languageMenu.Items[3]).IsChecked);
                AssertMenuPalette(window, languageMenu);
                Render(languageMenu, "combined-russian-light-language-menu.png");
                var saveError = languageMenu.Items.OfType<MenuItem>().Last();
                saveError.Visibility = Visibility.Visible;
                Pump(window);
                Assert.Equal(
                    ((SolidColorBrush) window.FindResource("QuietDisabledText")).Color,
                    ((SolidColorBrush) saveError.Foreground).Color);
                Assert.NotNull(saveError.Template.FindName("MenuSurface", saveError));
                languageMenu.IsOpen = false;

                var search = WpfTest.Descendants(layers.View).OfType<TextBox>().Single();
                search.Text = "Drawing";
                search.Focus();
                search.SelectAll();
                var editMenu = Assert.IsType<ContextMenu>(search.ContextMenu);
                editMenu.PlacementTarget = search;
                editMenu.IsOpen = true;
                Pump(window);
                Assert.Equal(
                    ["Вырезать", "Копировать", "Вставить", "Выбрать всё"],
                    editMenu.Items.OfType<MenuItem>().Select(item => item.Header));
                AssertMenuPalette(window, editMenu);
                Render(editMenu, "combined-russian-light-text-edit-menu.png");
                editMenu.IsOpen = false;
            }
            finally
            {
                window.Close();
                UiText.Current.Select(previousLanguage, persist: false);
            }
        });
    }

    private static void AssertAppearance(
        Popup popup,
        ComboBox theme,
        ComboBox palette,
        string title,
        string themeLabel,
        string[] themes,
        string[] palettes)
    {
        Assert.True(popup.IsOpen);
        string[] labels = [.. WpfTest.Descendants(popup.Child).OfType<TextBlock>().Select(block => block.Text)];
        Assert.Contains(title, labels);
        Assert.Contains(themeLabel, labels);
        Assert.Equal(themes, theme.Items.OfType<ComboBoxItem>().Select(item => item.Content));
        Assert.Equal(palettes, palette.Items.OfType<ComboBoxItem>().Select(item => item.Content));
    }

    private static void AssertMenuPalette(FrameworkElement window, ContextMenu menu)
    {
        var text = ((SolidColorBrush) window.FindResource("QuietText")).Color;
        var muted = ((SolidColorBrush) window.FindResource("QuietDisabledText")).Color;
        var surface = ((SolidColorBrush) window.FindResource("QuietSurface")).Color;
        Assert.Equal(surface, ((SolidColorBrush) menu.Background).Color);
        Assert.Equal(text, ((SolidColorBrush) menu.Foreground).Color);

        foreach (var item in menu.Items.OfType<MenuItem>().Where(item => item.Visibility == Visibility.Visible))
            Assert.Equal(item.IsEnabled ? text : muted, ((SolidColorBrush) item.Foreground).Color);
    }

    private static void Pump(FrameworkElement element) =>
        element.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void Render(FrameworkElement element, string filename)
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
        using var output = File.Create(Path.Combine(AppContext.BaseDirectory, filename));
        encoder.Save(output);
    }
}
