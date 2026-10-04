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

/// <summary>Exercises the actual filter popup bindings at the minimum panel width.</summary>
[Collection("Language changes")]
public sealed class PropertyFilterPresentationTests
{
    /// <summary>The popup edits and applies typed conditions in both themes and languages.</summary>
    [Theory]
    [InlineData(LanguagePreference.English, "Dark")]
    [InlineData(LanguagePreference.English, "Light")]
    [InlineData(LanguagePreference.Russian, "Dark")]
    [InlineData(LanguagePreference.Russian, "Light")]
    public void PopupAppliesConditionAtMinimumWidth(LanguagePreference language, string theme) => WpfTest.Run(() =>
    {
        var previous = UiText.Current.Preference;
        using var settings = new SettingsFile();
        using var lens = new ObjectExplorerLens(new PropertyFilterTests.Actions(), DrawingGrouping.ObjectTypes);
        using var shell = new ExplorerViewModel([lens]);
        var appearance = new AppearancePreferences(settings.Service) {Theme = theme};
        var window = new ExplorerWindow(shell, appearance) {ShowActivated = false, Left = -10000, Top = -10000};

        try
        {
            UiText.Current.Select(language, persist: false);
            shell.ToggleLensCommand.ExecuteAsync(shell.Lenses[0]).GetAwaiter().GetResult();
            var model = lens.ViewModel;
            model.EnterCommand.ExecuteAsync(Assert.Single(model.Items)).GetAwaiter().GetResult();
            window.Show();
            window.Width = 300;
            window.Height = 450;
            Pump();
            var view = Assert.IsType<ObjectExplorerView>(shell.ActiveView);
            var toggle = Assert.IsType<ToggleButton>(view.FindName("PropertyFilterAction"));
            var popup = Assert.IsType<Popup>(view.FindName("PropertyFilterPopup"));
            toggle.IsChecked = true;
            Pump();
            Assert.True(popup.IsOpen);
            var content = Assert.IsAssignableFrom<FrameworkElement>(popup.Child);
            var controls = WpfTest.Descendants(content).OfType<ComboBox>().ToArray();
            var property = Assert.Single(controls, control => control.SelectedValuePath == "Id");
            property.SelectedValue = DrawingPropertyId.Length;
            Pump();
            var comparison = Assert.Single(controls, control =>
                control.ItemsSource is IEnumerable<PropertyFilterOperatorOption>);
            comparison.SelectedValue = DrawingFilterOperator.GreaterThan;
            var input = Assert.Single(WpfTest.Descendants(content).OfType<TextBox>());
            input.Text = "5";
            Pump();
            Assert.Equal(DrawingPropertyId.Length, model.PropertyFilter.PropertyId);
            Assert.Equal(DrawingFilterOperator.GreaterThan, model.PropertyFilter.Operator);
            Assert.Equal("5", model.PropertyFilter.InputText);
            var apply = Assert.Single(
                WpfTest.Descendants(content).OfType<Button>(),
                button => ReferenceEquals(button.Command, model.ApplyPropertyFilterCommand));
            Assert.Equal(UiText.Current.Get("Apply"), apply.Content);
            Assert.True(apply.IsEnabled);
            apply.Command.Execute(null);
            model.ApplyPropertyFilterCommand.ExecutionTask!.GetAwaiter().GetResult();
            Pump();
            Assert.Equal(["2", "3"], model.Current!.Objects.Select(id => id.DisplayId));
            Assert.InRange(content.ActualWidth, 250, 300);
            Assert.InRange(content.ActualHeight, 1, 450);
            Assert.All(controls.Where(control => control.IsVisible), control =>
            {
                Assert.True(control.ActualWidth > 0);
                Assert.True(control.ActualHeight >= 24);
                Assert.Equal(
                    Assert.IsType<SolidColorBrush>(window.FindResource("QuietText")).Color,
                    Assert.IsType<SolidColorBrush>(control.Foreground).Color);
            });
            Save(content, $"filter-{language}-{theme}-popup");
            var alternate = language == LanguagePreference.English ? LanguagePreference.Russian : LanguagePreference.English;
            input.Text = language == LanguagePreference.English ? "1.5" : "1,5";
            UiText.Current.Select(alternate, persist: false);
            Pump();
            Assert.Equal(alternate == LanguagePreference.English ? "1.5" : "1,5", input.Text);
            Assert.Equal(["2", "3"], model.Current.Objects.Select(id => id.DisplayId));
            UiText.Current.Select(language, persist: false);
            input.Text = "5";
            property.SelectedValue = DrawingPropertyId.Linetype;
            Pump();
            var choice = Assert.IsType<ComboBox>(view.FindName("PropertyFilterValue"));
            choice.SelectedValue = new DrawingTextValue("ByLayer", true);
            UiText.Current.Select(alternate, persist: false);
            Pump();
            Assert.Equal(new DrawingTextValue("ByLayer", true), choice.SelectedValue);
            Assert.Contains(
                UiText.Current.Get("ByLayer"),
                WpfTest.Descendants(choice).OfType<TextBlock>().Select(block => block.Text));
            UiText.Current.Select(language, persist: false);
            popup.IsOpen = false;
            Pump();
            var list = Assert.Single(WpfTest.Descendants(view).OfType<ListBox>());
            Save(Assert.IsAssignableFrom<FrameworkElement>(window.Content), $"filter-{language}-{theme}-panel");
            Assert.True(list.ActualHeight >= 24, $"The object list is only {list.ActualHeight} pixels high.");
        }
        finally
        {
            window.Close();
            UiText.Current.Select(previous, persist: false);
        }
    });

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void Save(FrameworkElement content, string name)
    {
        var bitmap = new RenderTargetBitmap(
            (int) Math.Ceiling(content.ActualWidth),
            (int) Math.Ceiling(content.ActualHeight),
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "property-filter"));
        Directory.CreateDirectory(directory);
        using var output = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(output);
    }
}
