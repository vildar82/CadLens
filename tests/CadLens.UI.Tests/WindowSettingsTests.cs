using System.IO;
using System.Text.Json;
using System.Windows;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>Expanded panel dimensions survive window and host lifetimes without saving compact size.</summary>
[Collection("WPF")]
public sealed class WindowSettingsTests
{
    /// <summary>Closing expanded saves the final size; a new session still starts compact.</summary>
    [Fact]
    public void ExpandedCloseRestoresDimensionsInANewSession()
    {
        WpfTest.Run(() =>
        {
            using var file = new SettingsFile();
            using var model = CreateModel();
            var window = new ExplorerWindow(model, new AppearancePreferences(file.Service)) {Left = 10, Top = 20};
            ToggleLens(model);
            window.Width = 420;
            window.Height = 700;
            Assert.False(File.Exists(SettingsPath(file)));
            window.Close();

            using var saved = JsonDocument.Parse(File.ReadAllText(SettingsPath(file)));
            Assert.Equal(["Width", "Height"], saved.RootElement.EnumerateObject().Select(property => property.Name));
            using var reopenedModel = CreateModel();
            var reopened = new ExplorerWindow(reopenedModel, new AppearancePreferences(new SettingsService(file.Directory)));
            Assert.Equal((340, 52), (reopened.Width, reopened.Height));
            Assert.True(double.IsNaN(reopened.Left) && double.IsNaN(reopened.Top));
            ToggleLens(reopenedModel);
            Assert.Equal((420, 700), (reopened.Width, reopened.Height));
            reopened.Close();
        });
    }

    /// <summary>Collapse saves before closing, while compact close preserves the last expanded dimensions.</summary>
    [Fact]
    public void CollapseSavesDimensionsWithoutPersistingCompactSize()
    {
        WpfTest.Run(() =>
        {
            using var file = new SettingsFile();
            using var model = CreateModel();
            var window = new ExplorerWindow(model, settings: file.Service);
            ToggleLens(model);
            window.Width = 460;
            window.Height = 720;
            ToggleLens(model);
            var saved = File.ReadAllText(SettingsPath(file));
            Assert.Equal((340, 52), (window.Width, window.Height));
            ToggleLens(model);
            window.Width = 480;
            Assert.Equal(saved, File.ReadAllText(SettingsPath(file)));
            ToggleLens(model);
            saved = File.ReadAllText(SettingsPath(file));
            window.Close();
            Assert.Equal(saved, File.ReadAllText(SettingsPath(file)));

            using var reopenedModel = CreateModel();
            var reopened = new ExplorerWindow(reopenedModel, settings: new SettingsService(file.Directory));
            ToggleLens(reopenedModel);
            Assert.Equal((480, 720), (reopened.Width, reopened.Height));
            reopened.Close();
        });
    }

    /// <summary>Host shutdown deactivates the model before closing the window and keeps the expanded size.</summary>
    [Fact]
    public void HostShutdownCapturesDimensionsBeforeCollapsing()
    {
        WpfTest.Run(() =>
        {
            using var file = new SettingsFile();
            using var model = CreateModel();
            var window = new ExplorerWindow(model, settings: file.Service);
            ToggleLens(model);
            window.Width = 440;
            window.Height = 710;
            model.Close(true);
            window.Close();

            using var reopenedModel = CreateModel();
            var reopened = new ExplorerWindow(reopenedModel, settings: new SettingsService(file.Directory));
            ToggleLens(reopenedModel);
            Assert.Equal((440, 710), (reopened.Width, reopened.Height));
            reopened.Close();
        });
    }

    /// <summary>Invalid JSON, missing dimensions, and invalid numbers cannot break opening or activation.</summary>
    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"Width\":420}")]
    [InlineData("{\"Width\":-1,\"Height\":700}")]
    [InlineData("{\"Width\":420,\"Height\":0}")]
    [InlineData("{\"Width\":\"NaN\",\"Height\":700}")]
    [InlineData("{\"Width\":1e400,\"Height\":700}")]
    public void InvalidDimensionsUseDefaults(string json)
    {
        WpfTest.Run(() =>
        {
            using var file = new SettingsFile();
            File.WriteAllText(SettingsPath(file), json);
            using var model = CreateModel();
            var window = new ExplorerWindow(model, settings: file.Service);
            Assert.Equal((340, 52), (window.Width, window.Height));
            ToggleLens(model);
            Assert.Equal(
                (Math.Clamp(370, window.MinWidth, window.MaxWidth), Math.Clamp(660, window.MinHeight, window.MaxHeight)),
                (window.Width, window.Height));
            window.Close();
        });
    }

    /// <summary>Saved dimensions are clamped to both the minimum panel size and current monitor work area.</summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(1e308, 1e308)]
    public void RestoredDimensionsFitTheCurrentMonitor(double width, double height)
    {
        WpfTest.Run(() =>
        {
            using var file = new SettingsFile();
            file.Service.Save("window-size.json", new {Width = width, Height = height});
            using var model = CreateModel();
            var window = new ExplorerWindow(model, settings: file.Service);
            ToggleLens(model);
            Assert.Equal(Math.Clamp(width, window.MinWidth, window.MaxWidth), window.Width);
            Assert.Equal(Math.Clamp(height, window.MinHeight, window.MaxHeight), window.Height);
            Assert.True(window.Width <= SystemParameters.WorkArea.Width);
            Assert.True(window.Height <= SystemParameters.WorkArea.Height);
            window.Close();
        });
    }

    /// <summary>A blocked settings path leaves collapse, session restoration, and later saving usable.</summary>
    [Fact]
    public void FailedSaveKeepsTheSizeForTheSessionAndCanRecover()
    {
        WpfTest.Run(() =>
        {
            using var file = new SettingsFile();
            Directory.CreateDirectory(SettingsPath(file));
            using var model = CreateModel();
            var window = new ExplorerWindow(model, settings: file.Service);
            ToggleLens(model);
            window.Width = 450;
            window.Height = 730;
            ToggleLens(model);
            Assert.Equal((340, 52), (window.Width, window.Height));
            ToggleLens(model);
            Assert.Equal((450, 730), (window.Width, window.Height));
            Directory.Delete(SettingsPath(file));
            window.Close();

            using var reopenedModel = CreateModel();
            var reopened = new ExplorerWindow(reopenedModel, settings: new SettingsService(file.Directory));
            ToggleLens(reopenedModel);
            Assert.Equal((450, 730), (reopened.Width, reopened.Height));
            reopened.Close();
        });
    }

    private static ExplorerViewModel CreateModel() =>
        new([new CounterLens(new CounterViewModel(new CounterService()))]);

    private static void ToggleLens(ExplorerViewModel model) =>
        model.ToggleLensCommand.ExecuteAsync(model.Lenses[0]).GetAwaiter().GetResult();

    private static string SettingsPath(SettingsFile file) => Path.Combine(file.Directory, "window-size.json");
}
