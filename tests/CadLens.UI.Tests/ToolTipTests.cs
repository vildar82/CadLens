using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CadLens.Lenses;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>Checks complete tooltip text in the separate popup tree.</summary>
[Collection("Language changes")]
public sealed class ToolTipTests
{
    /// <summary>Both lens descriptions and live status remain readable in either window state and language.</summary>
    [Theory]
    [InlineData(DrawingGrouping.Layers, false)]
    [InlineData(DrawingGrouping.Layers, true)]
    [InlineData(DrawingGrouping.ObjectTypes, false)]
    [InlineData(DrawingGrouping.ObjectTypes, true)]
    public void LensToolTipsDescribeTheLensAndKeepLiveStatus(DrawingGrouping grouping, bool expanded)
    {
        WpfTest.Run(() =>
        {
            var previousLanguage = UiText.Current.Preference;
            using var file = new SettingsFile();
            using var lens = new ObjectExplorerLens(new AppearanceTests.Actions(), grouping);
            using var model = new ExplorerViewModel([lens]);
            var window = new ExplorerWindow(model, new AppearancePreferences(file.Service));

            try
            {
                UiText.Current.Select(LanguagePreference.English, persist: false);

                if (expanded)
                    model.ToggleLensCommand.ExecuteAsync(model.Lenses[0]).GetAwaiter().GetResult();

                window.Show();
                Pump(window);
                var button = WpfTest.Descendants(window).OfType<ToggleButton>()
                    .Single(candidate => ReferenceEquals(candidate.DataContext, model.Lenses[0]));
                var tooltip = Assert.IsType<ToolTip>(button.ToolTip);
                tooltip.PlacementTarget = button;
                tooltip.IsOpen = true;
                Pump(window);
                AssertLensText(tooltip, lens.Descriptor, model.Status);
                AssertReadable(tooltip);
                Render(tooltip, $"tooltip-{grouping}-English-{expanded}.png");

                UiText.Current.Select(LanguagePreference.Russian, persist: false);
                Pump(window);
                AssertLensText(tooltip, lens.Descriptor, model.Status);
                AssertReadable(tooltip);
                Render(tooltip, $"tooltip-{grouping}-Russian-{expanded}.png");

                model.ToggleLensCommand.ExecuteAsync(model.Lenses[0]).GetAwaiter().GetResult();
                Pump(window);
                AssertLensText(tooltip, lens.Descriptor, model.Status);
                tooltip.IsOpen = false;
            }
            finally
            {
                window.Close();
                UiText.Current.Select(previousLanguage, persist: false);
            }
        });
    }

    /// <summary>Long action help wraps completely for string and explicit text content.</summary>
    [Theory]
    [InlineData(LanguagePreference.English, false)]
    [InlineData(LanguagePreference.English, true)]
    [InlineData(LanguagePreference.Russian, false)]
    [InlineData(LanguagePreference.Russian, true)]
    public void LongActionHelpWrapsWithoutClipping(LanguagePreference language, bool explicitText)
    {
        WpfTest.Run(() =>
        {
            var previousLanguage = UiText.Current.Preference;
            using var file = new SettingsFile();
            using var lens = new ObjectExplorerLens(new AppearanceTests.Actions(), DrawingGrouping.Layers);
            using var model = new ExplorerViewModel([lens]);
            var window = new ExplorerWindow(model, new AppearancePreferences(file.Service));

            try
            {
                UiText.Current.Select(language, persist: false);
                model.ToggleLensCommand.ExecuteAsync(model.Lenses[0]).GetAwaiter().GetResult();
                window.Show();
                Pump(window);
                var button = WpfTest.Descendants(lens.View).OfType<Button>()
                    .Single(candidate => AutomationProperties.GetName(candidate) == UiText.Current.Get("Focus camera"));
                var text = Assert.IsType<string>(button.ToolTip);
                var tooltip = new ToolTip
                {
                    Content = explicitText ? new TextBlock {Text = text} : text,
                    PlacementTarget = button,
                    Style = (Style) button.FindResource(typeof(ToolTip))
                };
                button.ToolTip = tooltip;
                tooltip.IsOpen = true;
                Pump(window);
                var block = Assert.Single(WpfTest.Descendants(tooltip).OfType<TextBlock>());
                Assert.True(block.ActualHeight > block.FontSize * 2);
                Assert.Equal(text, new TextRange(block.ContentStart, block.ContentEnd).Text);
                AssertReadable(tooltip);
                Render(tooltip, $"tooltip-focus-{language}-{explicitText}.png");
                tooltip.IsOpen = false;
            }
            finally
            {
                window.Close();
                UiText.Current.Select(previousLanguage, persist: false);
            }
        });
    }

    private static void AssertLensText(ToolTip tooltip, LensDescriptor descriptor, string status)
    {
        var blocks = WpfTest.Descendants(tooltip).OfType<TextBlock>().ToArray();
        Assert.Equal(
            [UiText.Current.Get(descriptor.Label), UiText.Current.Get(descriptor.Description), status],
            blocks.Select(block => block.Text));

        if (UiText.Current.Preference == LanguagePreference.Russian)
            Assert.NotEqual(descriptor.Description, UiText.Current.Get(descriptor.Description));
    }

    private static void AssertReadable(ToolTip tooltip)
    {
        Assert.InRange(tooltip.ActualWidth, 1, 320);

        foreach (var block in WpfTest.Descendants(tooltip).OfType<TextBlock>())
        {
            Assert.Equal(TextWrapping.Wrap, block.TextWrapping);
            Assert.Equal(TextTrimming.None, block.TextTrimming);
            var required = new TextBlock
            {
                Text = block.Text,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = block.FontFamily,
                FontSize = block.FontSize,
                FontWeight = block.FontWeight
            };
            required.Measure(new Size(block.ActualWidth, double.PositiveInfinity));
            Assert.True(block.ActualHeight >= required.DesiredSize.Height - 0.1);
            var origin = block.TranslatePoint(new Point(), tooltip);
            Assert.InRange(origin.X, 0, tooltip.ActualWidth - block.ActualWidth + 0.1);
            Assert.InRange(origin.Y, 0, tooltip.ActualHeight - block.ActualHeight + 0.1);
        }
    }

    private static void Pump(FrameworkElement element) =>
        element.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void Render(FrameworkElement element, string name)
    {
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
}
