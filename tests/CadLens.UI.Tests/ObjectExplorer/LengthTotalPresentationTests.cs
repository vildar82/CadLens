using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CadLens.Common;
using CadLens.Lenses;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>Length totals use drawing precision and remain readable in the compact shared explorer.</summary>
[Collection("Language changes")]
public sealed class LengthTotalPresentationTests
{
    /// <summary>Summation precedes display rounding, and both languages distinguish unavailable values.</summary>
    [Theory]
    [InlineData(LanguagePreference.English, "Total length: 2.47 drawing units · 1 unavailable")]
    [InlineData(LanguagePreference.Russian, "Общая длина: 2,47 ед. чертежа · Недоступно: 1")]
    public void FormatsRawTotalWithDrawingPrecision(LanguagePreference language, string expected)
    {
        var previous = UiText.Current.Preference;

        try
        {
            UiText.Current.Select(language, persist: false);
            var group = Assert.Single(DrawingLensProvider.Build(Inventory(), DrawingGrouping.ObjectTypes, []).Groups);
            Assert.Equal(expected, DrawingValueFormatter.FormatLengthTotal(group.LengthTotal, new DrawingPrecision(2)));
            var unavailable = DrawingValueFormatter.FormatLengthTotal(new DrawingLengthTotal(null, 3));
            Assert.Contains("—", unavailable);
            Assert.Contains("3", unavailable);
            Assert.DoesNotContain("0", unavailable);
            Assert.Equal(string.Empty, DrawingValueFormatter.FormatLengthTotal(null));
        }
        finally
        {
            UiText.Current.Select(previous, persist: false);
        }
    }

    /// <summary>Real row and breadcrumb bindings update after navigation, filtering, and a language switch.</summary>
    [Theory]
    [InlineData(DrawingGrouping.Layers, LanguagePreference.English, "Dark")]
    [InlineData(DrawingGrouping.Layers, LanguagePreference.Russian, "Light")]
    [InlineData(DrawingGrouping.ObjectTypes, LanguagePreference.English, "Light")]
    [InlineData(DrawingGrouping.ObjectTypes, LanguagePreference.Russian, "Dark")]
    public void TotalsFitAtMinimumWidthAndUpdateWithoutReading(
        DrawingGrouping grouping,
        LanguagePreference language,
        string theme) => WpfTest.Run(() =>
    {
        var previous = UiText.Current.Preference;
        using var settings = new SettingsFile();
        var actions = new PropertyFilterTests.Actions {InventoryOverride = Inventory()};
        using var lens = new ObjectExplorerLens(actions, grouping);
        using var shell = new ExplorerViewModel([lens]);
        var appearance = new AppearancePreferences(settings.Service) {Theme = theme};
        var window = new ExplorerWindow(shell, appearance) {ShowActivated = false, Left = -10000, Top = -10000};

        try
        {
            UiText.Current.Select(language, persist: false);
            shell.ToggleLensCommand.ExecuteAsync(shell.Lenses[0]).GetAwaiter().GetResult();
            var model = lens.ViewModel;
            window.Show();
            window.Width = 300;
            window.Height = 450;
            Pump();
            var view = Assert.IsType<ObjectExplorerView>(shell.ActiveView);
            var expected = DrawingValueFormatter.FormatLengthTotal(new DrawingLengthTotal(2.468, 1), model.Precision);
            var rowTotal = Assert.Single(
                WpfTest.Descendants(view).OfType<TextBlock>(),
                block => block.Text == expected);
            Assert.True(rowTotal.IsVisible);
            Assert.InRange(rowTotal.ActualWidth, 1, 276);
            Assert.InRange(rowTotal.ActualHeight, 1, 48);
            model.EnterCommand.ExecuteAsync(Assert.Single(model.Items)).GetAwaiter().GetResult();
            if (grouping == DrawingGrouping.Layers)
                model.EnterCommand.ExecuteAsync(Assert.Single(model.Items)).GetAwaiter().GetResult();

            Pump();
            var current = Assert.IsType<TextBlock>(view.FindName("CurrentLengthTotal"));
            Assert.Equal(expected, current.Text);
            Assert.True(current.IsVisible);
            model.PropertyFilter.PropertyId = DrawingPropertyId.Length;
            model.PropertyFilter.Operator = DrawingFilterOperator.GreaterThan;
            model.PropertyFilter.InputText = "1";
            model.ApplyPropertyFilterCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            Pump();
            Assert.Equal(new DrawingLengthTotal(2.468, 0), model.Current!.LengthTotal);
            Assert.Equal(
                DrawingValueFormatter.FormatLengthTotal(model.Current.LengthTotal, model.Precision),
                current.Text);
            UiText.Current.Select(
                language == LanguagePreference.English ? LanguagePreference.Russian : LanguagePreference.English,
                persist: false);
            Pump();
            Assert.Equal(
                DrawingValueFormatter.FormatLengthTotal(model.Current.LengthTotal, model.Precision),
                current.Text);
            Assert.Equal(1, actions.ReadCount);
            var list = Assert.Single(WpfTest.Descendants(view).OfType<ListBox>());
            Assert.True(list.ActualHeight >= 24, $"The object list is only {list.ActualHeight} pixels high.");
            model.EnterCommand.ExecuteAsync(model.Items[0]).GetAwaiter().GetResult();
            Pump();
            Assert.Equal(Visibility.Collapsed, current.Visibility);
        }
        finally
        {
            window.Close();
            UiText.Current.Select(previous, persist: false);
        }
    });

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static DrawingInventory Inventory()
    {
        var layer = new LayerId("Roads");
        return new DrawingInventory(
            "Model",
            [new LayerSnapshot(layer, "Roads", false, false, false, false)],
            [Entity(1, 1.234), Entity(2, 1.234), Entity(3, null)],
            new DrawingPrecision(2));

        EntitySnapshot Entity(int id, double? length) => new(
            new TestEntityId(id),
            layer,
            "AcDbPolyline",
            new Dictionary<DrawingPropertyId, DrawingValue?>
            {
                [DrawingPropertyId.Length] =
                    length is { } value ? new DrawingNumberValue(value, DrawingUnit.Distance) : null
            }.ToImmutableDictionary());
    }

    private sealed record LayerId(string DisplayId) : ILayerId;
}