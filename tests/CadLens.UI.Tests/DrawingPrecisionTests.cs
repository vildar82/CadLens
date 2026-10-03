using System.Collections.Immutable;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CadLens.Lenses;
using Common;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>Drawing precision changes presentation while preserving exact exploration values.</summary>
[Collection("WPF")]
public sealed class DrawingPrecisionTests
{
    /// <summary>Decimal and angular precision apply consistently without rounding counts or assigned lineweights.</summary>
    [Theory]
    [InlineData(LanguagePreference.English, "12.35", "12.3°", "1.23", "0.25 mm")]
    [InlineData(LanguagePreference.Russian, "12,35", "12,3°", "1,23", "0,25 мм")]
    public void FormatsTypedValuesWithDrawingPrecision(
        LanguagePreference language,
        string length,
        string angle,
        string scale,
        string lineweight)
    {
        var previous = UiText.Current.Preference;

        try
        {
            UiText.Current.Select(language, persist: false);
            var precision = new DrawingPrecision(2, 1);
            var value = new DrawingNumberValue(12.34567, DrawingUnit.Distance);
            Assert.Equal(length, DrawingValueFormatter.FormatValue(value, precision));
            Assert.Equal(
                length,
                DrawingValueFormatter.FormatDetail(
                    new DetailField("Length", "", DetailValueKind.TypedValue, value),
                    precision));
            Assert.Equal(
                angle,
                DrawingValueFormatter.FormatValue(
                    new DrawingNumberValue(12.34567 * Math.PI / 180, DrawingUnit.Angle),
                    precision));
            Assert.Equal(
                scale,
                DrawingValueFormatter.FormatValue(new DrawingNumberValue(1.234567, DrawingUnit.Scale), precision));
            Assert.Equal(
                "123",
                DrawingValueFormatter.FormatValue(new DrawingNumberValue(123, DrawingUnit.Count), precision));
            Assert.Equal(
                "0",
                DrawingValueFormatter.FormatValue(new DrawingNumberValue(-0.004, DrawingUnit.Distance), precision));
            Assert.Equal(
                lineweight,
                DrawingValueFormatter.FormatValue(
                    new DrawingLineweightValue(new AssignedLineweight(AssignedLineweightKind.Explicit, 25)),
                    new DrawingPrecision(0)));
            Assert.Equal(12.34567, value.Value);
        }
        finally
        {
            UiText.Current.Select(previous, persist: false);
        }
    }

    /// <summary>Nonfinite input cannot leak into numeric labels even when the formatter is called directly.</summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonfiniteValuesDisplayAsUnavailable(double value)
    {
        foreach (var unit in Enum.GetValues<DrawingUnit>())
            Assert.Equal("—", DrawingValueFormatter.FormatValue(new DrawingNumberValue(value, unit)));
    }

    /// <summary>Unrepresentable degree values use the unavailable marker without changing the stored angle.</summary>
    [Theory]
    [InlineData(double.MaxValue)]
    [InlineData(double.MinValue)]
    public void AnglesThatOverflowDegreesDisplayAsUnavailable(double radians)
    {
        var value = new DrawingNumberValue(radians, DrawingUnit.Angle);

        Assert.Equal("—", DrawingValueFormatter.FormatValue(value));
        Assert.Equal(radians, value.Value);
    }

    /// <summary>Large valid degree values remain finite despite an overflowing intermediate multiplication.</summary>
    [Theory]
    [InlineData(1e306, 5.729577951308232e307)]
    [InlineData(-1e306, -5.729577951308232e307)]
    public void LargeFiniteAnglesRemainRepresentable(double radians, double expectedDegrees)
    {
        var value = new DrawingNumberValue(radians, DrawingUnit.Angle);
        var text = DrawingValueFormatter.FormatValue(value);
        Assert.EndsWith("°", text);
        var displayed = double.Parse(text[..^1], NumberStyles.Float, UiText.Current.Culture);

        Assert.True(double.IsFinite(displayed));
        Assert.InRange(Math.Abs((displayed - expectedDegrees) / expectedDegrees), 0, 1e-14);
        Assert.Equal(radians, value.Value);
    }

    /// <summary>Ascending transparency values follow the displayed percentage rather than native opacity.</summary>
    [Fact]
    public void TransparencySortingFollowsDisplayedPercentages()
    {
        List<DrawingValue> values =
        [
            new DrawingTransparencyValue(new AssignedTransparency(AssignedTransparencyKind.Explicit, 0)),
            new DrawingTransparencyValue(new AssignedTransparency(AssignedTransparencyKind.Explicit, 102)),
            new DrawingTransparencyValue(new AssignedTransparency(AssignedTransparencyKind.Explicit))
        ];
        values.Sort(DrawingProperties.CompareValues);

        Assert.Equal(["0%", "60%", "100%"], values.Select(value => DrawingValueFormatter.FormatValue(value)));
    }

    /// <summary>Rows, captions, details, and tooltips use refreshed precision while raw grouping and sorting stay exact.</summary>
    [Theory]
    [InlineData(LanguagePreference.English, "12.34", "12.3412", "15.1°", "15.12°")]
    [InlineData(LanguagePreference.Russian, "12,34", "12,3412", "15,1°", "15,12°")]
    public void RefreshUpdatesBoundValuesAndKeepsExactGroups(
        LanguagePreference language,
        string rounded,
        string refreshed,
        string angle,
        string refreshedAngle)
    {
        var previous = UiText.Current.Preference;

        try
        {
            UiText.Current.Select(language, persist: false);
            WpfTest.Run(() =>
            {
                var actions = new Actions(CreateInventory());
                using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.ObjectTypes);
                model.ActivateAsync(CancellationToken.None).GetAwaiter().GetResult();
                model.EnterCommand.ExecuteAsync(Assert.Single(model.Items)).GetAwaiter().GetResult();
                model.SelectDisplayPropertyCommand.Execute(
                    model.DisplayPropertyOptions.Single(option => option.Id == DrawingPropertyId.Length));
                var view = new ObjectExplorerView(model);
                Layout(view);
                Assert.Equal(new DrawingPrecision(2, 1), model.Precision);
                Assert.Equal(["2", "1"], model.Items.Select(node => node.Id));
                var rows = WpfTest.Descendants(view).OfType<TextBlock>().Where(block => block.Text == rounded).ToList();
                Assert.Equal(2, rows.Count);
                Assert.All(rows, block => Assert.Equal(rounded, block.ToolTip));

                model.ToggleGroupingCommand
                    .ExecuteAsync(model.GroupingOptions.Single(option => option.Id == DrawingPropertyId.Length))
                    .GetAwaiter().GetResult();
                Layout(view);
                Assert.Equal(2, model.Items.Length);
                var identities = model.Items.Select(node => node.Id).Order(StringComparer.Ordinal).ToArray();
                var label = $"{UiText.Current.Get("Length")}: {rounded}";
                Assert.Equal(2, Text(view).Count(text => text == label));
                model.EnterCommand.ExecuteAsync(model.Items.Single(node => node.Objects.Contains(new TestEntityId(2))))
                    .GetAwaiter().GetResult();
                model.EnterCommand.ExecuteAsync(Assert.Single(model.Items)).GetAwaiter().GetResult();
                Layout(view);
                Assert.Contains(rounded, Text(view));
                Assert.Contains(angle, Text(view));

                actions.Inventory = actions.Inventory with {Precision = new DrawingPrecision(4, 2)};
                model.ReadCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                Layout(view);
                Assert.Equal("2", model.Current!.Id);
                Assert.Equal(new DrawingPrecision(4, 2), model.Precision);
                Assert.Contains(refreshed, Text(view));
                Assert.Contains(refreshedAngle, Text(view));
                model.BackCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                model.BackCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                Assert.Equal(identities, model.Items.Select(node => node.Id).Order(StringComparer.Ordinal));
                Assert.Equal(2, actions.ReadCount);
            });
        }
        finally
        {
            UiText.Current.Select(previous, persist: false);
        }
    }

    private static void Layout(FrameworkElement view)
    {
        view.Measure(new Size(370, 660));
        view.Arrange(new Rect(0, 0, 370, 660));
        view.UpdateLayout();
    }

    private static IEnumerable<string> Text(FrameworkElement view) =>
        WpfTest.Descendants(view).OfType<TextBlock>().Select(block => block.Text);

    private static DrawingInventory CreateInventory()
    {
        var layer = new LayerId("Roads");

        return new DrawingInventory(
            "Model",
            [new LayerSnapshot(layer, "Roads", false, false, false, false)],
            [Arc(1, 12.3448), Arc(2, 12.3412)],
            new DrawingPrecision(2, 1));

        EntitySnapshot Arc(int id, double length) => new(
            new TestEntityId(id),
            layer,
            "AcDbArc",
            ImmutableDictionary<DrawingPropertyId, DrawingValue?>.Empty
                .Add(DrawingPropertyId.Length, new DrawingNumberValue(length, DrawingUnit.Distance))
                .Add(DrawingPropertyId.Radius, new DrawingNumberValue(5, DrawingUnit.Distance))
                .Add(
                    DrawingPropertyId.StartAngle,
                    new DrawingNumberValue(15.123456 * Math.PI / 180, DrawingUnit.Angle)),
            DrawingPropertyId.Radius);
    }

    private sealed record LayerId(string DisplayId) : ILayerId;

    private sealed class Actions(DrawingInventory inventory) : IObjectExplorerActions
    {
        internal DrawingInventory Inventory { get; set; } = inventory;
        internal int ReadCount { get; private set; }

        public Task<HostResult<LensPresentation>> ReadAsync(
            DrawingGrouping grouping,
            IReadOnlySet<string> enabledFilters,
            CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult<HostResult<LensPresentation>>(
                new HostResult<LensPresentation>.Success(
                    DrawingLensProvider.Build(Inventory, grouping, enabledFilters)));
        }

        public void ClearImmediately(bool hostTerminating)
        {
        }

        public Task<HostResult<bool>> ClearAsync(CancellationToken cancellationToken) => Success();

        public Task<HostResult<bool>> ClearIsolationAsync(CancellationToken cancellationToken) => Success();

        public Task<HostResult<bool>> SelectAsync(
            ImmutableArray<IPlacedObjectId> objects,
            CancellationToken cancellationToken) => Success();

        public Task<string> IsolateObjectsAsync(
            ImmutableArray<IPlacedObjectId> objects,
            CancellationToken cancellationToken) => Task.FromResult("");

        public Task<string> FocusAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken) =>
            Task.FromResult("");

        private static Task<HostResult<bool>> Success() =>
            Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(true));
    }
}