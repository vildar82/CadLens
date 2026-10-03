using System.Collections;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Resources;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CadLens.Lenses;
using Common;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>Language switching is app-local and must not run alongside other window checks.</summary>
[CollectionDefinition("Language changes", DisableParallelization = true)]
public sealed class LanguageCollection;

/// <summary>Checks persisted choices, resource fallback, and live localized drawing views.</summary>
[Collection("Language changes")]
public sealed partial class LanguageTests : IDisposable
{
    private readonly SettingsFile _file = new();

    private readonly LanguagePreference _previous = UiText.Current.Preference;

    /// <summary>Display language selection does not depend on regional format or host UI culture.</summary>
    [Theory]
    [InlineData("ru-RU", "ru")]
    [InlineData("ru", "ru")]
    [InlineData("en-GB", "en")]
    [InlineData("es-ES", "en")]
    [InlineData("zh-CN", "en")]
    [InlineData("", "en")]
    public void WindowsDisplayLanguageChoosesSupportedCulture(string displayLanguage, string expected)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-ES");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            var text = new UiText(_file.Service, () => displayLanguage);
            Assert.Equal(LanguagePreference.Windows, text.Preference);
            Assert.Equal(expected, text.Culture.Name);
            text.Select(LanguagePreference.Russian, persist: false);
            Assert.Equal("es-ES", CultureInfo.CurrentCulture.Name);
            Assert.Equal("de-DE", CultureInfo.CurrentUICulture.Name);
            Assert.Equal("Слои", text.Get("Layers"));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    /// <summary>Explicit choices survive reopening and Windows mode follows the current display language.</summary>
    [Fact]
    public void ChoicePersistsWithoutReplacingWindowsMode()
    {
        var windowsLanguage = new WindowsLanguage();
        var text = new UiText(_file.Service, windowsLanguage.Read);
        text.Select(LanguagePreference.Russian);
        Assert.Equal(LanguagePreference.Russian, new UiText(_file.Service, () => "en").Preference);
        text.Select(LanguagePreference.English);
        Assert.Equal("en", new UiText(_file.Service, () => "ru-RU").Culture.Name);
        text.Select(LanguagePreference.Windows);
        windowsLanguage.Name = "ru-RU";
        var reopened = new UiText(_file.Service, windowsLanguage.Read);
        Assert.Equal(LanguagePreference.Windows, reopened.Preference);
        Assert.Equal("ru", reopened.Culture.Name);
        windowsLanguage.Name = "es-ES";
        reopened.Select(LanguagePreference.Windows);
        Assert.Equal("en", reopened.Culture.Name);
        Assert.Empty(reopened.PreferenceError);
    }

    /// <summary>A malformed or future preference never prevents the window from starting.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("{broken}")]
    [InlineData("\"German\"")]
    [InlineData("\"99\"")]
    public void InvalidPreferenceUsesWindows(string contents)
    {
        File.WriteAllText(PreferencePath, contents);
        var text = new UiText(_file.Service, () => "ru-RU");
        Assert.Equal(LanguagePreference.Windows, text.Preference);
        Assert.Equal("ru", text.Culture.Name);
    }

    /// <summary>A failed preference write keeps the selected language and explains the session-only change.</summary>
    [Fact]
    public void FailedSaveKeepsSessionLanguage()
    {
        Directory.CreateDirectory(PreferencePath);
        var text = new UiText(_file.Service, () => "en-US");
        text.Select(LanguagePreference.Russian);
        Assert.Equal("ru", text.Culture.Name);
        Assert.Contains("Не удалось сохранить", text.PreferenceError);
        Assert.Empty(Directory.GetFiles(_file.Directory, "*.tmp"));
    }

    /// <summary>Russian satellite resources cover every English entry and preserve format arguments.</summary>
    [Fact]
    public void RussianSatelliteHasCompleteResourceSet()
    {
        var russian = CultureInfo.GetCultureInfo("ru");
        Assert.Equal("ru", typeof(UiText).Assembly.GetSatelliteAssembly(russian).GetName().CultureName);
        var resources = new ResourceManager("CadLens.UI.Localization.Strings", typeof(UiText).Assembly);
        var english = resources.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        var translated = resources.GetResourceSet(russian, true, false)!;
        string[] englishKeys = [.. english.Cast<DictionaryEntry>().Select(entry => (string) entry.Key).Order()];
        string[] russianKeys = [.. translated.Cast<DictionaryEntry>().Select(entry => (string) entry.Key).Order()];
        Assert.Equal(englishKeys, russianKeys);

        foreach (var key in englishKeys)
        {
            var value = translated.GetString(key);
            Assert.False(string.IsNullOrWhiteSpace(value), key);
            Assert.Equal(FormatArguments(key), FormatArguments(value));
        }

        var text = new UiText(_file.Service, () => "ru-RU");
        Assert.Equal("Unknown app phrase", text.Get("Unknown app phrase"));
    }

    /// <summary>Language changes preserve drawing names, navigation, and effects while refreshing metadata.</summary>
    [Fact]
    public void LiveSwitchPreservesDrawingStateAndTranslatesDetails()
    {
        WpfTest.Run(() =>
        {
            UiText.Current.Select(LanguagePreference.English, persist: false);
            var actions = new Actions();
            using var layers = new ObjectExplorerLens(actions, DrawingGrouping.Layers);
            using var objects = new ObjectExplorerLens(actions, DrawingGrouping.ObjectTypes);
            using var shell = new ExplorerViewModel([layers, objects]);
            var window = new ExplorerWindow(shell, settings: _file.Service);
            shell.ToggleLensCommand.ExecuteAsync(shell.Lenses[0]).GetAwaiter().GetResult();
            var model = layers.ViewModel;
            model.ToggleFilterCommand
                .ExecuteAsync(model.Filters.Single(option => option.Descriptor.Id == DrawingLensProvider.IncludeOff))
                .GetAwaiter()
                .GetResult();
            model.ToggleFilterCommand
                .ExecuteAsync(model.Filters.Single(option => option.Descriptor.Id == DrawingLensProvider.IncludeFrozen))
                .GetAwaiter()
                .GetResult();
            var rootContent = (FrameworkElement) window.Content;
            UiText.Current.Select(LanguagePreference.Russian, persist: false);
            Layout(rootContent, 370, 660);
            string[] rootTexts = [.. WpfTest.Descendants(rootContent).OfType<TextBlock>().Select(block => block.Text)];
            Assert.Contains("Слоёв: 1", rootTexts);
            Assert.Contains("Объектов: 1", rootTexts);
            SavePreview(rootContent, "russian-layers-root", 370, 660);
            Layout(rootContent, 300, 450);
            SavePreview(rootContent, "russian-layers-root-minimum", 300, 450);
            UiText.Current.Select(LanguagePreference.English, persist: false);
            model.EnterCommand.ExecuteAsync(Assert.Single(model.Items)).GetAwaiter().GetResult();
            model.EnterCommand.ExecuteAsync(Assert.Single(model.Items)).GetAwaiter().GetResult();
            model.EnterCommand.ExecuteAsync(Assert.Single(model.Items)).GetAwaiter().GetResult();
            model.ToggleAutoSelectCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            model.ToggleAutoIsolationCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            var current = model.Current;
            var selected = actions.Selected;
            var isolated = actions.Isolated;
            var calls = actions.Calls;

            UiText.Current.Select(LanguagePreference.Russian, persist: false);
            var content = (FrameworkElement) window.Content;
            Layout(content, 370, 660);
            string[] texts = [.. WpfTest.Descendants(content).OfType<TextBlock>().Select(block => block.Text)];
            Assert.Contains("Слои", texts);
            Assert.Contains("Тип примитива", texts);
            Assert.Contains("Слой", texts);
            Assert.Contains("Да", texts);
            Assert.Contains("Visibility", texts);
            Assert.Contains("Line", texts);
            Assert.Contains(
                "Скрыт: отключён, заморожен на текущем видовом экране. Включение в список и фокус не меняют видимость.",
                texts);
            Assert.Equal("Line 10", model.Current!.Label);
            Assert.Equal("Visibility", model.DisplaySpaceLabel);
            Assert.Contains("Другие объекты временно скрыты", model.Status);
            Assert.Equal("1 из 1", model.ObjectPosition);
            Assert.Same(current, model.Current);
            Assert.Equal(selected, actions.Selected);
            Assert.Equal(isolated, actions.Isolated);
            Assert.Equal(calls, actions.Calls);
            Assert.True(model.IsAutoSelect);
            Assert.True(model.IsAutoIsolation);
            SavePreview(content, "russian-object-details", 370, 660);
            Layout(content, 300, 450);
            SavePreview(content, "russian-object-details-minimum", 300, 450);

            UiText.Current.Select(LanguagePreference.English, persist: false);
            Layout(content, 370, 660);
            Assert.Equal("1 of 1", model.ObjectPosition);
            Assert.Contains("Other objects hidden temporarily", model.Status);
            Assert.Same(current, model.Current);
            window.Close();
        });
    }

    /// <summary>A layout name matching an app message stays raw until the drawing context is lost.</summary>
    [Fact]
    public void LosingDrawingLocalizesSpaceStatusEvenWhenRawNameMatches()
    {
        WpfTest.Run(() =>
        {
            UiText.Current.Select(LanguagePreference.Russian, persist: false);
            using var lens = new ObjectExplorerLens(new Actions("No active drawing"), DrawingGrouping.Layers);
            using var shell = new ExplorerViewModel([lens]);
            var window = new ExplorerWindow(shell, settings: _file.Service);
            shell.ToggleLensCommand.ExecuteAsync(shell.Lenses[0]).GetAwaiter().GetResult();
            var content = (FrameworkElement) window.Content;
            Layout(content, 370, 660);
            Assert.Contains(
                "No active drawing",
                WpfTest.Descendants(content).OfType<TextBlock>().Select(block => block.Text));
            lens.ViewModel.ResetContextAsync(false).GetAwaiter().GetResult();
            Layout(content, 370, 660);
            Assert.Contains(
                "Нет текущего чертежа",
                WpfTest.Descendants(content).OfType<TextBlock>().Select(block => block.Text));
            Assert.DoesNotContain(
                "No active drawing",
                WpfTest.Descendants(content).OfType<TextBlock>().Select(block => block.Text));
            window.Close();
        });
    }

    /// <summary>Both lens choices and the language menu fit the compact Russian bar.</summary>
    [Fact]
    public void RussianCompactBarAndLanguageMenuRender()
    {
        WpfTest.Run(() =>
        {
            UiText.Current.Select(LanguagePreference.Russian, persist: false);
            var actions = new Actions();
            using var layers = new ObjectExplorerLens(actions, DrawingGrouping.Layers);
            using var objects = new ObjectExplorerLens(actions, DrawingGrouping.ObjectTypes);
            using var shell = new ExplorerViewModel([layers, objects]);
            var window = new ExplorerWindow(shell, settings: _file.Service);
            var content = (FrameworkElement) window.Content;
            Layout(content, 340, 52);
            var toggleLensCommand = shell.ToggleLensCommand;
            ToggleButton[] buttons =
            [
                .. WpfTest.Descendants(content).OfType<ToggleButton>()
                    .Where(button => ReferenceEquals(button.Command, toggleLensCommand))
            ];
            Assert.Equal(["Слои", "Объекты"], buttons.Select(button => button.Content));
            var scroller = WpfTest.Descendants(content).OfType<ScrollViewer>().Single();
            Assert.True(
                scroller.ExtentWidth <= scroller.ViewportWidth,
                "Russian lens choices must fit without scrolling.");
            var language = WpfTest.Descendants(content).OfType<Button>().Single(button => Equals(button.Content, "RU"));
            var menu = Assert.IsType<ContextMenu>(language.ContextMenu);
            menu.Resources = window.Resources;
            Assert.Equal("Как в Windows", Assert.IsType<MenuItem>(menu.Items[0]).Header);
            Assert.True(Assert.IsType<MenuItem>(menu.Items[3]).IsChecked);
            Layout(menu, 260, 220);
            SavePreview(content, "russian-compact", 340, 52);
            SavePreview(menu, "russian-language-menu", 260, 220);
            window.Close();
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        UiText.Current.Select(_previous, persist: false);

        _file.Dispose();
    }

    private string PreferencePath => Path.Combine(_file.Directory, "language.json");

    private static string[] FormatArguments(string text) =>
        [.. FormatArgumentPattern().Matches(text).Select(match => match.Value).Order()];

    [GeneratedRegex(@"\{\d+(?:[^}]*)\}")]
    private static partial Regex FormatArgumentPattern();

    private static void Layout(FrameworkElement content, int width, int height)
    {
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
    }

    private static void SavePreview(FrameworkElement content, string name, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "artifacts",
                "modes-language"));
        Directory.CreateDirectory(directory);
        using var output = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(output);
    }

    private sealed class WindowsLanguage
    {
        internal string Name { get; set; } = "en-US";

        internal string Read() => Name;
    }

    private sealed record LayerId(string DisplayId) : ILayerId;

    private sealed class Source(string spaceLabel) : IDrawingInventorySource
    {
        public Task<HostResult<DrawingInventory>> ReadAsync(CancellationToken cancellationToken)
        {
            var layer = new LayerId("1");
            var inventory = new DrawingInventory(
                spaceLabel,
                [new LayerSnapshot(layer, "Visibility", true, false, true, true)],
                [new EntitySnapshot(new TestEntityId(10), layer, "AcDbLine")]);

            return Task.FromResult<HostResult<DrawingInventory>>(new HostResult<DrawingInventory>.Success(inventory));
        }
    }

    private sealed class Actions : IObjectExplorerActions
    {
        private readonly DrawingLensProvider _provider;

        internal Actions(string spaceLabel = "Visibility") =>
            _provider = new DrawingLensProvider(new Source(spaceLabel));

        internal int Calls { get; private set; }
        internal ImmutableArray<IPlacedObjectId> Selected { get; private set; } = [];
        internal ImmutableArray<IPlacedObjectId> Isolated { get; private set; } = [];

        public Task<HostResult<LensPresentation>> ReadAsync(
            DrawingGrouping grouping,
            IReadOnlySet<string> enabledFilters,
            CancellationToken cancellationToken)
        {
            Calls++;
            return _provider.LoadAsync(grouping, enabledFilters, cancellationToken);
        }

        public Task<HostResult<bool>> ClearAsync(CancellationToken cancellationToken)
        {
            Calls++;
            Selected = [];
            Isolated = [];
            return Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(true));
        }

        public Task<HostResult<bool>> ClearIsolationAsync(CancellationToken cancellationToken)
        {
            Calls++;
            Isolated = [];
            return Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(true));
        }

        public Task<string> IsolateObjectsAsync(
            ImmutableArray<IPlacedObjectId> objects,
            CancellationToken cancellationToken)
        {
            Calls++;
            Isolated = objects;
            return Task.FromResult("Other objects hidden temporarily. Originally hidden objects remain hidden.");
        }

        public Task<string> FocusAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken) =>
            Task.FromResult("View fitted.");

        public Task<HostResult<bool>> SelectAsync(
            ImmutableArray<IPlacedObjectId> objects,
            CancellationToken cancellationToken)
        {
            Calls++;
            Selected = objects;
            return Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(true));
        }

        public void ClearImmediately(bool hostTerminating)
        {
        }
    }
}