using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CadLens.Lenses;
using CadLens.Common;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>Checks the production presentation of typed metrics and composite groups.</summary>
[Collection("Language changes")]
public sealed class DrawingPresentationTests
{
    /// <summary>Both custom property popups center their labels and retain themed controls with keyboard focus.</summary>
    [Theory]
    [InlineData(LanguagePreference.English, "Dark")]
    [InlineData(LanguagePreference.English, "Light")]
    [InlineData(LanguagePreference.Russian, "Dark")]
    [InlineData(LanguagePreference.Russian, "Light")]
    public void OpenPropertyPopupsUseThemedCenteredRows(LanguagePreference language, string theme)
    {
        var previous = UiText.Current.Preference;

        try
        {
            UiText.Current.Select(language, persist: false);
            WpfTest.Run(() =>
            {
                using var file = new SettingsFile();
                using var lens = new ObjectExplorerLens(new Actions(), DrawingGrouping.ObjectTypes);
                using var shell = new ExplorerViewModel([lens]);
                shell.ToggleLensCommand.ExecuteAsync(shell.Lenses[0]).GetAwaiter().GetResult();
                var model = lens.ViewModel;
                model.EnterCommand.ExecuteAsync(Assert.Single(model.Items)).GetAwaiter().GetResult();
                var appearance = new AppearancePreferences(file.Service) {Theme = theme};
                var window = new ExplorerWindow(shell, appearance) {ShowActivated = false, Left = -10000, Top = -10000};

                try
                {
                    window.Show();
                    window.Width = 300;
                    window.Height = 660;
                    window.Activate();
                    FlushDispatcher();
                    var content = Assert.IsType<ObjectExplorerView>(shell.ActiveView);
                    var selector = Assert.IsType<ToggleButton>(content.FindName("GroupingAction"));
                    var popup = Assert.IsType<Popup>(content.FindName("GroupingPopup"));
                    Click(selector);
                    FlushDispatcher();
                    var popupContent = Assert.IsAssignableFrom<FrameworkElement>(popup.Child);
                    var checkBox = WpfTest.Descendants(popupContent).OfType<CheckBox>()
                        .Single(box => box.CommandParameter is GroupingOption {Id.BuiltIn: DrawingPropertyId.Color});
                    Click(checkBox);
                    FlushDispatcher();
                    model.ToggleGroupingCommand.ExecutionTask?.GetAwaiter().GetResult();
                    checkBox = WpfTest.Descendants(popupContent).OfType<CheckBox>()
                        .Single(box => box.CommandParameter is GroupingOption {Id.BuiltIn: DrawingPropertyId.Color});
                    var glyphBorder = Assert.IsType<Border>(checkBox.Template.FindName("GlyphBorder", checkBox));
                    var glyph = Assert.IsType<System.Windows.Shapes.Path>(
                        checkBox.Template.FindName("CheckGlyph", checkBox));
                    var label = Assert.IsType<ContentPresenter>(checkBox.Template.FindName("LabelHost", checkBox));
                    var focusRing = Assert.IsType<Border>(checkBox.Template.FindName("FocusRing", checkBox));
                    Assert.True(checkBox.IsChecked);
                    Assert.Equal(Visibility.Visible, glyph.Visibility);
                    Assert.Equal(
                        Brush(checkBox, "QuietAccent").Color,
                        Assert.IsType<SolidColorBrush>(glyph.Stroke).Color);
                    Assert.Equal(
                        Brush(checkBox, "QuietAccent").Color,
                        Assert.IsType<SolidColorBrush>(glyphBorder.BorderBrush).Color);
                    Assert.Equal(
                        Brush(checkBox, "QuietSelected").Color,
                        Assert.IsType<SolidColorBrush>(glyphBorder.Background).Color);
                    Assert.Equal(VerticalAlignment.Center, label.VerticalAlignment);
                    Assert.True(checkBox.ActualHeight >= 24);
                    Assert.InRange(Math.Abs(Center(glyphBorder, checkBox) - Center(label, checkBox)), 0, 0.5);
                    Keyboard.Focus(checkBox);
                    FlushDispatcher();
                    Assert.True(checkBox.IsKeyboardFocused);
                    Assert.Equal(Visibility.Visible, focusRing.Visibility);
                    Assert.Equal(
                        Brush(checkBox, "QuietFocus").Color,
                        Assert.IsType<SolidColorBrush>(focusRing.BorderBrush).Color);
                    Save(
                        popupContent,
                        300,
                        language,
                        $"grouping-{theme}",
                        (int) Math.Ceiling(popupContent.ActualHeight));
                    checkBox.IsEnabled = false;
                    Assert.Equal(
                        Brush(checkBox, "QuietDisabledText").Color,
                        Assert.IsType<SolidColorBrush>(glyph.Stroke).Color);
                    Assert.Equal(Visibility.Collapsed, focusRing.Visibility);
                    popup.IsOpen = false;
                    model.EnterCommand.ExecuteAsync(Assert.Single(model.Items)).GetAwaiter().GetResult();
                    FlushDispatcher();
                    var propertySelector = Assert.IsType<ToggleButton>(content.FindName("DisplayPropertyAction"));
                    var propertyPopup = Assert.IsType<Popup>(content.FindName("DisplayPropertyPopup"));
                    Click(propertySelector);
                    FlushDispatcher();
                    var propertyContent = Assert.IsAssignableFrom<FrameworkElement>(propertyPopup.Child);
                    var selectedButton = WpfTest.Descendants(propertyContent).OfType<Button>()
                        .Single(button => button.CommandParameter is GroupingOption {IsSelected: true});
                    var mark = WpfTest.Descendants(selectedButton).OfType<TextBlock>()
                        .Single(block => block.Text == "✓");
                    var caption = WpfTest.Descendants(selectedButton).OfType<TextBlock>()
                        .Single(block => block.Text != "✓");
                    Assert.Equal(VerticalAlignment.Center, mark.VerticalAlignment);
                    Assert.Equal(VerticalAlignment.Center, caption.VerticalAlignment);
                    Assert.InRange(Math.Abs(Center(mark, selectedButton) - Center(caption, selectedButton)), 0, 0.5);
                    Save(
                        propertyContent,
                        300,
                        language,
                        $"property-{theme}",
                        (int) Math.Ceiling(propertyContent.ActualHeight));
                    propertyPopup.IsOpen = false;
                }
                finally
                {
                    window.Close();
                }
            });
        }
        finally
        {
            UiText.Current.Select(previous, persist: false);
        }
    }

    /// <summary>A real explorer window opens populated grouping options and retains multiple choices through object navigation.</summary>
    [Theory]
    [InlineData(300)]
    [InlineData(370)]
    public void OpenProductionDropdownAndSelectMultipleProperties(int width)
    {
        WpfTest.Run(() =>
        {
            using var lens = new ObjectExplorerLens(new Actions(), DrawingGrouping.ObjectTypes);
            using var shell = new ExplorerViewModel([lens]);
            shell.ToggleLensCommand.ExecuteAsync(shell.Lenses[0]).GetAwaiter().GetResult();
            var model = lens.ViewModel;
            model.EnterCommand.ExecuteAsync(Assert.Single(model.Items)).GetAwaiter().GetResult();
            using var windowSettings = new SettingsFile();
            var window = new ExplorerWindow(shell, settings: windowSettings.Service) {ShowActivated = false, Left = -10000, Top = -10000};

            try
            {
                window.Show();
                window.Width = width;
                window.Height = 660;
                FlushDispatcher();
                var content = (ObjectExplorerView) shell.ActiveView!;
                var selector = Assert.IsType<ToggleButton>(content.FindName("GroupingAction"));
                var popup = Assert.IsType<Popup>(content.FindName("GroupingPopup"));
                Assert.True(selector.IsVisible);
                Click(selector);
                FlushDispatcher();
                Assert.True(popup.IsOpen);
                Assert.NotNull(PresentationSource.FromVisual(popup.Child));
                var popupContent = (FrameworkElement) popup.Child;
                var options = Assert.Single(WpfTest.Descendants(popupContent).OfType<ItemsControl>());
                Assert.NotNull(options.ItemsSource);
                Assert.True(options.Items.Count >= 2);

                foreach (var id in new[] {DrawingPropertyId.Color, DrawingPropertyId.Linetype})
                {
                    var checkBox = WpfTest.Descendants(popupContent).OfType<CheckBox>()
                        .Single(box => box.CommandParameter is GroupingOption option && option.Id == id);
                    Assert.Same(model.ToggleGroupingCommand, checkBox.Command);
                    Click(checkBox);
                    FlushDispatcher();
                    model.ToggleGroupingCommand.ExecutionTask?.GetAwaiter().GetResult();
                    Assert.True(popup.IsOpen);
                }

                Assert.All(
                    model.GroupingOptions.Where(option =>
                        option.Id.BuiltIn is DrawingPropertyId.Color or DrawingPropertyId.Linetype),
                    option => Assert.True(option.IsSelected));
                Assert.Contains(UiText.Current.Get("Color"), model.GroupingSummary);
                Assert.Contains(UiText.Current.Get("Linetype"), model.GroupingSummary);
                Assert.Contains(model.GroupingSummary, Text(selector));
                Save((FrameworkElement) window.Content, width, UiText.Current.Preference, "open-dropdown");
                Save(
                    popupContent,
                    width,
                    UiText.Current.Preference,
                    "dropdown-options",
                    (int) Math.Ceiling(popupContent.ActualHeight));
                popup.IsOpen = false;
                model.EnterCommand.ExecuteAsync(Assert.Single(model.Items)).GetAwaiter().GetResult();
                FlushDispatcher();
                var propertySelector = Assert.IsType<ToggleButton>(content.FindName("DisplayPropertyAction"));
                var propertyPopup = Assert.IsType<Popup>(content.FindName("DisplayPropertyPopup"));
                Assert.True(propertySelector.IsVisible);
                Click(propertySelector);
                FlushDispatcher();
                Assert.True(propertyPopup.IsOpen);
                var propertyOptions = Assert.Single(WpfTest.Descendants(propertyPopup.Child).OfType<ItemsControl>());
                Assert.NotNull(propertyOptions.ItemsSource);
                var lengthButton = WpfTest.Descendants(propertyPopup.Child).OfType<Button>()
                    .Single(button => button.CommandParameter is GroupingOption {Id.BuiltIn: DrawingPropertyId.Length});
                Assert.Same(model.SelectDisplayPropertyCommand, lengthButton.Command);
                Click(lengthButton);
                FlushDispatcher();
                Assert.False(propertyPopup.IsOpen);
                Assert.Equal((DrawingPropertyKey) DrawingPropertyId.Length, model.DisplayPropertyId);
                Assert.Contains("42.5", Text(content));
                Assert.Contains("125.25", Text(content));
                Assert.DoesNotContain("7", Text(content));
                Assert.DoesNotContain("15", Text(content));
                Assert.Equal(2, model.GroupingOptions.Count(option => option.IsSelected));
                Assert.Contains(UiText.Current.Get("Length"), Text(propertySelector));
                Save((FrameworkElement) window.Content, width, UiText.Current.Preference, "selected-length");
                Click(propertySelector);
                FlushDispatcher();
                Assert.True(propertyPopup.IsOpen);
                Assert.True(
                    ((GroupingOption) WpfTest.Descendants(propertyPopup.Child).OfType<Button>()
                        .Single(button => button.CommandParameter is GroupingOption {Id.BuiltIn: DrawingPropertyId.Length})
                        .CommandParameter).IsSelected);
                propertyPopup.IsOpen = false;
                model.EnterCommand.ExecuteAsync(model.Items[0]).GetAwaiter().GetResult();
                FlushDispatcher();
                Assert.True(model.IsObject);
                Assert.True(selector.IsVisible);
                Click(selector);
                FlushDispatcher();
                Assert.True(popup.IsOpen);
                Assert.True(options.Items.Count >= 2);
                Assert.Equal(2, model.GroupingOptions.Count(option => option.IsSelected));
                popup.IsOpen = false;
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>Real popup activation repeatedly changes rendered builtin, attribute, and dynamic values.</summary>
    [Theory]
    [InlineData(DrawingGrouping.Layers)]
    [InlineData(DrawingGrouping.ObjectTypes)]
    public void PropertyPopupSwitchesRenderedValuesRepeatedly(DrawingGrouping grouping)
    {
        var previous = UiText.Current.Preference;

        try
        {
            UiText.Current.Select(LanguagePreference.English, persist: false);
            WpfTest.Run(() =>
            {
                var layer = new LayerId("Raw layer");
                var attribute = DrawingPropertyKey.ForAttribute("MARK");
                var dynamic = DrawingPropertyKey.ForDynamicBlock("Width");
                var inventory = new DrawingInventory(
                    "Model space",
                    [new LayerSnapshot(layer, layer.DisplayId, false, false, false, false)],
                    [.. Enumerable.Range(1, 2).Select(index => new EntitySnapshot(
                        new TestEntityId(index),
                        layer,
                        "AcDbBlockReference",
                        ImmutableDictionary<DrawingPropertyId, DrawingValue?>.Empty
                            .Add(DrawingPropertyId.BlockName, new DrawingTextValue("Door"))
                            .Add(DrawingPropertyId.Attributes, new DrawingNumberValue(index, DrawingUnit.Count)),
                        DrawingPropertyId.Attributes,
                        BlockAttributes: [new BlockAttributeSnapshot("MARK", index == 1 ? "A" : "B")],
                        DynamicBlockProperties: [new DynamicBlockPropertySnapshot(
                            "Width", new DrawingNumberValue(index == 1 ? 42.5 : 125.25, DrawingUnit.Distance))]))]);
                using var lens = new ObjectExplorerLens(new Actions(inventory), grouping);
                using var shell = new ExplorerViewModel([lens]);
                shell.ToggleLensCommand.ExecuteAsync(shell.Lenses[0]).GetAwaiter().GetResult();
                var model = lens.ViewModel;

                if (grouping == DrawingGrouping.Layers)
                    model.EnterCommand.ExecuteAsync(Assert.Single(model.Items)).GetAwaiter().GetResult();

                model.EnterCommand.ExecuteAsync(Assert.Single(model.Items)).GetAwaiter().GetResult();
                using var windowSettings = new SettingsFile();
                var window = new ExplorerWindow(shell, settings: windowSettings.Service)
                {
                    ShowActivated = false, Left = -10000, Top = -10000
                };

                try
                {
                    window.Show();
                    window.Width = 300;
                    window.Height = 660;
                    FlushDispatcher();
                    var content = (ObjectExplorerView) shell.ActiveView!;
                    var selector = Assert.IsType<ToggleButton>(content.FindName("DisplayPropertyAction"));
                    var popup = Assert.IsType<Popup>(content.FindName("DisplayPropertyPopup"));
                    Select(DrawingPropertyId.Layer, "Raw layer", "Raw layer");
                    Select(attribute, "A", "B");
                    Select(dynamic, "42.5", "125.25");
                    Select(DrawingPropertyId.Attributes, "1", "2");
                    Select(dynamic, "42.5", "125.25");
                    ToggleBlockGrouping();
                    Assert.False(selector.IsEnabled);
                    Assert.Equal("Open a group to choose the property shown in object rows", selector.ToolTip);
                    Assert.True(ToolTipService.GetShowOnDisabled(selector));
                    Assert.Equal(dynamic, model.DisplayPropertyId);
                    model.EnterCommand.ExecuteAsync(Assert.Single(model.Items)).GetAwaiter().GetResult();
                    FlushDispatcher();
                    Assert.True(selector.IsEnabled);
                    Select(attribute, "A", "B");
                    Select(dynamic, "42.5", "125.25");
                    model.BackCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                    FlushDispatcher();
                    Assert.False(selector.IsEnabled);
                    Assert.Equal(dynamic, model.DisplayPropertyId);
                    ToggleBlockGrouping();
                    Assert.True(selector.IsEnabled);
                    Select(DrawingPropertyId.Attributes, "1", "2");

                    void ToggleBlockGrouping()
                    {
                        model.ToggleGroupingCommand.ExecuteAsync(model.GroupingOptions.Single(option =>
                            option.Id == DrawingPropertyId.BlockName)).GetAwaiter().GetResult();
                        FlushDispatcher();
                    }

                    void Select(DrawingPropertyKey key, params string[] expected)
                    {
                        Click(selector);
                        FlushDispatcher();
                        Assert.True(popup.IsOpen);
                        var button = WpfTest.Descendants(popup.Child).OfType<Button>()
                            .Single(option => option.CommandParameter is GroupingOption item && item.Id == key);
                        var invoke = Assert.IsAssignableFrom<IInvokeProvider>(
                            new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke));
                        invoke.Invoke();
                        FlushDispatcher();
                        Assert.False(popup.IsOpen);
                        Assert.Equal(key, model.DisplayPropertyId);
                        Assert.Equal(expected, WpfTest.Descendants(content).OfType<TextBlock>()
                            .Where(block => block.DataContext is LensNode {Kind: LensNodeKind.Object} &&
                                            DockPanel.GetDock(block) == Dock.Right && block.ToolTip is string)
                            .Select(block => block.Text));
                    }
                }
                finally
                {
                    window.Close();
                }
            });
        }
        finally
        {
            UiText.Current.Select(previous, persist: false);
        }
    }

    /// <summary>Long selected text stays inside its column and remains available in the tooltip.</summary>
    [Theory]
    [InlineData(300)]
    [InlineData(370)]
    public void SelectTextPropertyInProductionWindow(int width)
    {
        WpfTest.Run(() =>
        {
            const string rawText = "Drawing text: a long sentence kept exactly as stored in the drawing.";
            var layer = new LayerId("Text layer");
            var properties = ImmutableDictionary<DrawingPropertyId, DrawingValue?>.Empty
                .Add(DrawingPropertyId.Text, new DrawingTextValue(rawText))
                .Add(DrawingPropertyId.TextStyle, new DrawingTextValue("Layers"))
                .Add(DrawingPropertyId.TextHeight, new DrawingNumberValue(2.5, DrawingUnit.Distance));
            var inventory = new DrawingInventory(
                "Model space",
                [new LayerSnapshot(layer, layer.DisplayId, false, false, false, false)],
                [new EntitySnapshot(new TestEntityId(8), layer, "AcDbText", properties, DrawingPropertyId.TextHeight)]);
            using var lens = new ObjectExplorerLens(new Actions(inventory), DrawingGrouping.ObjectTypes);
            using var shell = new ExplorerViewModel([lens]);
            shell.ToggleLensCommand.ExecuteAsync(shell.Lenses[0]).GetAwaiter().GetResult();
            var model = lens.ViewModel;
            model.EnterCommand.ExecuteAsync(Assert.Single(model.Items)).GetAwaiter().GetResult();
            using var windowSettings = new SettingsFile();
            var window = new ExplorerWindow(shell, settings: windowSettings.Service) {ShowActivated = false, Left = -10000, Top = -10000};

            try
            {
                window.Show();
                window.Width = width;
                window.Height = 660;
                FlushDispatcher();
                var content = (ObjectExplorerView) shell.ActiveView!;
                var selector = Assert.IsType<ToggleButton>(content.FindName("DisplayPropertyAction"));
                var popup = Assert.IsType<Popup>(content.FindName("DisplayPropertyPopup"));
                Click(selector);
                FlushDispatcher();
                Assert.True(popup.IsOpen);
                var button = WpfTest.Descendants(popup.Child).OfType<Button>()
                    .Single(option => option.CommandParameter is GroupingOption {Id.BuiltIn: DrawingPropertyId.Text});
                Click(button);
                FlushDispatcher();
                Assert.False(popup.IsOpen);
                Assert.Equal((DrawingPropertyKey) DrawingPropertyId.Text, model.DisplayPropertyId);
                var rowValue = Assert.Single(
                    WpfTest.Descendants(content).OfType<TextBlock>(),
                    block => block is {Text: rawText, ToolTip: string});
                Assert.Equal(rawText, rowValue.ToolTip);
                Assert.Equal(TextTrimming.CharacterEllipsis, rowValue.TextTrimming);
                Assert.InRange(rowValue.ActualWidth, 1, 120);
                Assert.InRange(rowValue.ActualHeight, 1, 18);
                Assert.Contains("Text 8", Text(content));
                Save((FrameworkElement) window.Content, width, UiText.Current.Preference, "selected-text");
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>Type lists show metrics and property groups keep placed counts at both compact widths.</summary>
    [Theory]
    [InlineData(300, LanguagePreference.English)]
    [InlineData(370, LanguagePreference.English)]
    [InlineData(300, LanguagePreference.Russian)]
    [InlineData(370, LanguagePreference.Russian)]
    public void RenderCompositeGroupsAndObjectMetrics(int width, LanguagePreference language)
    {
        var previous = UiText.Current.Preference;

        try
        {
            UiText.Current.Select(language, persist: false);
            WpfTest.Run(() =>
            {
                using var lens = new ObjectExplorerLens(new Actions(), DrawingGrouping.ObjectTypes);
                lens.ActivateAsync(CancellationToken.None).GetAwaiter().GetResult();
                var model = lens.ViewModel;
                model.EnterCommand.ExecuteAsync(Assert.Single(model.Items)).GetAwaiter().GetResult();
                var content = new ObjectExplorerView(model);
                Layout(content, width);
                Assert.Contains(UiText.Current.Get("Vertices"), Text(content));
                Assert.Contains("7", Text(content));
                Assert.Contains("15", Text(content));
                Assert.Contains("—", Text(content));
                Assert.Equal(
                    UiText.Current.Get("Unavailable"),
                    WpfTest.Descendants(content).OfType<TextBlock>().Single(block => block.Text == "—").ToolTip);
                Assert.DoesNotContain("1", Text(content));

                foreach (var id in new[] {DrawingPropertyId.Color, DrawingPropertyId.Linetype})
                    model.ToggleGroupingCommand.ExecuteAsync(model.GroupingOptions.Single(option => option.Id == id))
                        .GetAwaiter().GetResult();

                Layout(content, width);
                var popup = Assert.IsType<Popup>(content.FindName("GroupingPopup"));
                var popupContent = (FrameworkElement) popup.Child;
                Layout(popupContent, 250);
                var colorCheckBox = WpfTest.Descendants(popupContent).OfType<CheckBox>()
                    .Single(checkBox => checkBox.CommandParameter is GroupingOption {Id.BuiltIn: DrawingPropertyId.Color});
                Assert.Same(model.ToggleGroupingCommand, colorCheckBox.Command);
                Assert.True(colorCheckBox.IsChecked);
                Assert.True(colorCheckBox.Command.CanExecute(colorCheckBox.CommandParameter));
                model.ToggleGroupingCommand.ExecuteAsync((GroupingOption) colorCheckBox.CommandParameter)
                    .GetAwaiter().GetResult();
                Assert.False(model.GroupingOptions.Single(option => option.Id == DrawingPropertyId.Color).IsSelected);
                Layout(popupContent, 250);
                Assert.False(
                    WpfTest.Descendants(popupContent).OfType<CheckBox>()
                        .Single(checkBox => checkBox.CommandParameter is GroupingOption {Id.BuiltIn: DrawingPropertyId.Color})
                        .IsChecked);
                model.ToggleGroupingCommand.ExecuteAsync(
                        model.GroupingOptions.Single(option => option.Id == DrawingPropertyId.Color))
                    .GetAwaiter().GetResult();
                Layout(content, width);
                var group = Assert.Single(model.Items);
                var label = DrawingValueFormatter.FormatLabel(group);
                Assert.Contains("Layers", label);
                Assert.Contains(UiText.Current.Get("ByLayer"), label);
                Assert.Contains(label, Text(content));
                Assert.Contains(UiText.Current.Get("Objects"), Text(content));
                Assert.Contains("3", Text(content));
                Save(content, width, language, "groups");

                model.EnterCommand.ExecuteAsync(group).GetAwaiter().GetResult();
                Layout(content, width);
                Assert.Contains(UiText.Current.Get("Vertices"), Text(content));
                Assert.Contains("7", Text(content));
                Assert.Contains("15", Text(content));
                Save(content, width, language, "objects");
            });
        }
        finally
        {
            UiText.Current.Select(previous, persist: false);
        }
    }

    /// <summary>Raw drawing text is preserved even when it matches a localization key.</summary>
    [Fact]
    public void DetailPoliciesPreserveDrawingTextAndTranslateAppText()
    {
        var previous = UiText.Current.Preference;

        try
        {
            UiText.Current.Select(LanguagePreference.Russian, persist: false);
            Assert.Equal("Layers", DrawingValueFormatter.FormatDetail(new DetailField("Locked", "Layers")));
            Assert.Equal("По слою", DrawingValueFormatter.FormatValue(new DrawingTextValue("ByLayer", true)));
            Assert.Equal("Layers", DrawingValueFormatter.FormatValue(new DrawingTextValue("Layers")));
            Assert.Equal(
                "90°",
                DrawingValueFormatter.FormatValue(new DrawingNumberValue(Math.PI / 2, DrawingUnit.Angle)));
            Assert.Equal(
                "0,25 мм",
                DrawingValueFormatter.FormatValue(
                    new DrawingLineweightValue(
                        new AssignedLineweight(AssignedLineweightKind.Explicit, 25))));
            Assert.Equal(
                "—",
                DrawingValueFormatter.FormatDetail(new DetailField("Length", "", DetailValueKind.TypedValue)));
        }
        finally
        {
            UiText.Current.Select(previous, persist: false);
        }
    }

    private static void Layout(FrameworkElement content, int width)
    {
        content.Measure(new Size(width, 660));
        content.Arrange(new Rect(0, 0, width, 660));
        content.UpdateLayout();
    }

    private static SolidColorBrush Brush(FrameworkElement scope, string key) =>
        Assert.IsType<SolidColorBrush>(scope.FindResource(key));

    private static double Center(FrameworkElement element, UIElement parent) =>
        element.TranslatePoint(new Point(0, element.ActualHeight / 2), parent).Y;

    private static void Click(ButtonBase button) =>
        button.GetType().GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);

    private static void FlushDispatcher() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static string[] Text(FrameworkElement content) =>
        [.. WpfTest.Descendants(content).OfType<TextBlock>().Select(block => block.Text)];

    private static void Save(
        FrameworkElement content,
        int width,
        LanguagePreference language,
        string level,
        int height = 660)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "drawing-presentation"));
        Directory.CreateDirectory(directory);
        using var output = File.Create(Path.Combine(directory, $"drawing-{width}-{language}-{level}.png"));
        encoder.Save(output);
    }

    private static DrawingInventory Inventory()
    {
        var layer = new LayerId("Raw layer");
        var properties = ImmutableDictionary<DrawingPropertyId, DrawingValue?>.Empty
            .Add(DrawingPropertyId.Color, new DrawingColorValue(new AssignedColor(AssignedColorKind.ByLayer)))
            .Add(DrawingPropertyId.Linetype, new DrawingTextValue("Layers"))
            .Add(
                DrawingPropertyId.Lineweight,
                new DrawingLineweightValue(new AssignedLineweight(AssignedLineweightKind.ByLayer)))
            .Add(DrawingPropertyId.LinetypeScale, new DrawingNumberValue(1, DrawingUnit.Scale))
            .Add(
                DrawingPropertyId.Transparency,
                new DrawingTransparencyValue(new AssignedTransparency(AssignedTransparencyKind.ByLayer)));

        return new DrawingInventory(
            "Model space",
            [new LayerSnapshot(layer, layer.DisplayId, false, false, false, false)],
            [
                new EntitySnapshot(
                    new TestEntityId(1),
                    layer,
                    "AcDbPolyline",
                    properties.Add(DrawingPropertyId.Vertices, new DrawingNumberValue(7, DrawingUnit.Count)).Add(
                        DrawingPropertyId.Length,
                        new DrawingNumberValue(42.5, DrawingUnit.Distance)),
                    DrawingPropertyId.Vertices),
                new EntitySnapshot(
                    new TestEntityId(2),
                    layer,
                    "AcDbPolyline",
                    properties.Add(DrawingPropertyId.Vertices, new DrawingNumberValue(15, DrawingUnit.Count)).Add(
                        DrawingPropertyId.Length,
                        new DrawingNumberValue(125.25, DrawingUnit.Distance)),
                    DrawingPropertyId.Vertices),
                new EntitySnapshot(
                    new TestEntityId(3),
                    layer,
                    "AcDbPolyline",
                    properties.Add(DrawingPropertyId.Vertices, null).Add(DrawingPropertyId.Length, null),
                    DrawingPropertyId.Vertices)
            ]);
    }

    private sealed record LayerId(string DisplayId) : ILayerId;

    private sealed class Actions(DrawingInventory? inventory = null) : IObjectExplorerActions
    {
        public Task<HostResult<LensPresentation>> ReadAsync(
            DrawingGrouping grouping,
            IReadOnlyCollection<string> enabledFilters,
            ImmutableArray<IPlacedObjectId>? selectedObjects,
            CancellationToken cancellationToken) =>
            Task.FromResult<HostResult<LensPresentation>>(
                new HostResult<LensPresentation>.Success(
                    DrawingLensProvider.Build(inventory ?? Inventory(), grouping, enabledFilters)));

        public Task<HostResult<bool>> SelectAsync(
            ImmutableArray<IPlacedObjectId> objects,
            CancellationToken cancellationToken) =>
            Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(true));

        public Task<HostResult<bool>> ClearIsolationAsync(CancellationToken cancellationToken) =>
            Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(true));

        public Task<HostResult<bool>> ClearAsync(CancellationToken cancellationToken) =>
            Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(true));

        public Task<string> IsolateObjectsAsync(
            ImmutableArray<IPlacedObjectId> objects,
            CancellationToken cancellationToken) =>
            Task.FromResult("");

        public Task<string> FocusAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken) =>
            Task.FromResult("");

        public Task<HostResult<ImmutableArray<IPlacedObjectId>>> RequestObjectsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<HostResult<ImmutableArray<IPlacedObjectId>>>(new HostResult<ImmutableArray<IPlacedObjectId>>.Success([]));

        public void ClearImmediately(bool hostTerminating)
        {
        }
    }
}