using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;
using CadLens.Common;
using CadLens.Lenses;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>Both lenses browse captured insertion attributes without changing native action identity.</summary>
[Collection("Language changes")]
public sealed class BlockAttributeDetailsTests
{
    private static readonly LayerId Layer = new("Layer");

    /// <summary>Browsing preserves captured values until Refresh, and host actions target only the insertion.</summary>
    [Theory]
    [InlineData(DrawingGrouping.Layers)]
    [InlineData(DrawingGrouping.ObjectTypes)]
    public async Task BrowsesCapturedAttributesAndRefreshesInsertionValues(DrawingGrouping grouping)
    {
        var actions = new Actions(CreateInventory("P-01"));
        using var model = new ObjectExplorerViewModel(actions, grouping);
        await model.ActivateAsync(CancellationToken.None);
        actions.Inventory = CreateInventory("Updated");
        await EnterBlock(model, grouping);

        Assert.Equal("P-01", Field(model, "MARK").Value);
        Assert.Equal(1, actions.ReadCount);
        await model.NextCommand.ExecuteAsync(null);
        Assert.Equal("P-02", Field(model, "MARK").Value);
        await model.PreviousCommand.ExecuteAsync(null);
        Assert.Equal("P-01", Field(model, "MARK").Value);
        Assert.Equal(1, model.Current!.Count);
        Assert.Equal(1, actions.ReadCount);
        Assert.Contains(model.DisplayPropertyOptions, option => option.Id == DrawingPropertyKey.ForAttribute("MARK"));
        await model.FocusCommand.ExecuteAsync(null);
        Assert.Equal(model.Current.Objects, actions.Targets);
        await model.SelectCommand.ExecuteAsync(null);
        Assert.Equal(model.Current.Objects, actions.Targets);
        await model.IsolateCommand.ExecuteAsync(null);
        Assert.Equal(model.Current.Objects, actions.Targets);
        await model.ReadCommand.ExecuteAsync(null);

        Assert.Equal(2, actions.ReadCount);
        Assert.Equal("Updated", Field(model, "MARK").Value);
        Assert.Equal(new TestEntityId(1), Assert.Single(model.Current!.Objects));
    }

    /// <summary>Complete long values and raw labels remain readable in the narrow production view in both languages.</summary>
    [Theory]
    [InlineData(LanguagePreference.English, DrawingGrouping.Layers)]
    [InlineData(LanguagePreference.English, DrawingGrouping.ObjectTypes)]
    [InlineData(LanguagePreference.Russian, DrawingGrouping.Layers)]
    [InlineData(LanguagePreference.Russian, DrawingGrouping.ObjectTypes)]
    public void RendersRawTagsAndCompleteValues(LanguagePreference language, DrawingGrouping grouping)
    {
        var previous = UiText.Current.Preference;

        try
        {
            UiText.Current.Select(language, persist: false);
            WpfTest.Run(() =>
            {
                using var file = new SettingsFile();
                using var lens = new ObjectExplorerLens(new Actions(CreateInventory("P-01")), grouping);
                using var shell = new ExplorerViewModel([lens]);
                shell.ToggleLensCommand.ExecuteAsync(shell.Lenses[0]).GetAwaiter().GetResult();
                EnterBlock(lens.ViewModel, grouping).GetAwaiter().GetResult();
                var window = new ExplorerWindow(shell, new AppearancePreferences(file.Service))
                {
                    ShowActivated = false, Left = -10000, Top = -10000
                };

                try
                {
                    window.Show();
                    window.Width = 300;
                    window.Height = 660;
                    window.UpdateLayout();
                    Assert.Equal(300, window.ActualWidth);
                    var detail = Assert.IsType<ScrollViewer>(Assert.IsType<ObjectExplorerView>(lens.View).FindName("ObjectDetails"));
                    var blocks = WpfTest.Descendants(detail).OfType<TextBlock>().ToList();
                    var tag = Assert.Single(blocks, block => block.DataContext is DetailField {IsLabelRaw: true, Label: "Layer"} &&
                        block.Text == (language == LanguagePreference.English ? "Attribute: Layer" : "Атрибут: Layer"));
                    var longValue = Assert.Single(blocks, block => block.Text == LongValue);
                    Assert.Equal(TextWrapping.Wrap, tag.TextWrapping);
                    Assert.Equal(TextWrapping.Wrap, longValue.TextWrapping);
                    Assert.Equal(TextTrimming.None, longValue.TextTrimming);
                    Assert.True(longValue.ActualHeight > 30);
                    Assert.True(detail.ScrollableHeight > 0);
                    Assert.Contains(blocks, block => block.Text == "Layers");
                    Assert.Contains(blocks, block => block.Text == UiText.Current.Get("(blank)"));
                    Assert.Contains(blocks, block => block.Text == UiText.Current.Get("Unavailable"));
                    detail.ScrollToBottom();
                    window.UpdateLayout();
                    Assert.InRange(Math.Abs(detail.VerticalOffset - detail.ScrollableHeight), 0, 1);
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

    /// <summary>Missing and empty collections have different localized states; ordinary objects gain no attribute section.</summary>
    [Theory]
    [InlineData(LanguagePreference.English)]
    [InlineData(LanguagePreference.Russian)]
    public void DistinguishesEmptyAndUnavailableAttributes(LanguagePreference language)
    {
        var previous = UiText.Current.Preference;

        try
        {
            UiText.Current.Select(language, persist: false);
            var inventory = CreateInventory("P-01");
            var block = inventory.Entities[0];
            var empty = block with {BlockAttributes = []};
            var unavailable = block with {BlockAttributes = default};
            var ordinary = new EntitySnapshot(block.Id, Layer, "AcDbLine");

            Assert.Equal(UiText.Current.Get("No attached attributes"), State(empty));
            Assert.Equal(UiText.Current.Get("Unavailable"), State(unavailable));
            Assert.DoesNotContain(Node(ordinary).Fields, field => field.Label == "Attribute values");

            string State(EntitySnapshot entity) => DrawingValueFormatter.FormatDetail(
                Assert.Single(Node(entity).Fields, field => field.Label == "Attribute values"));

            LensNode Node(EntitySnapshot entity) => DrawingLensProvider.Build(
                inventory with {Entities = [entity]}, DrawingGrouping.ObjectTypes, []).Groups[0].Children[0];
        }
        finally
        {
            UiText.Current.Select(previous, persist: false);
        }
    }

    private static string LongValue => "First line\n" + new string('Ж', 500) + "\nLast line";

    private static DetailField Field(ObjectExplorerViewModel model, string tag) =>
        Assert.Single(model.Current!.Fields, field => field.IsLabelRaw && field.Label == tag);

    private static async Task EnterBlock(ObjectExplorerViewModel model, DrawingGrouping grouping)
    {
        if (grouping == DrawingGrouping.Layers)
            await model.EnterCommand.ExecuteAsync(Assert.Single(model.Items));

        await model.EnterCommand.ExecuteAsync(Assert.Single(model.Items));
        await model.EnterCommand.ExecuteAsync(model.Items.Single(node => node.Id == "1"));
    }

    private static DrawingInventory CreateInventory(string mark) => new(
        "Model",
        [new LayerSnapshot(Layer, "Layer", false, false, false, false)],
        [Block(1, mark), Block(2, "P-02")]);

    private static EntitySnapshot Block(int id, string mark) => new(
        new TestEntityId(id),
        Layer,
        "AcDbBlockReference",
        new Dictionary<DrawingPropertyId, DrawingValue?>
        {
            [DrawingPropertyId.BlockName] = new DrawingTextValue("Equipment"),
            [DrawingPropertyId.Attributes] = new DrawingNumberValue(5, DrawingUnit.Count)
        }.ToImmutableDictionary(),
        BlockAttributes:
        [
            new BlockAttributeSnapshot("MARK", mark),
            new BlockAttributeSnapshot("Layer", "Layers"),
            new BlockAttributeSnapshot("BLANK", ""),
            new BlockAttributeSnapshot("MISSING", null),
            new BlockAttributeSnapshot("LONG_TAG_DESCRIPTION", LongValue)
        ]);

    private sealed record LayerId(string DisplayId) : ILayerId;

    private sealed class Actions(DrawingInventory inventory) : IObjectExplorerActions
    {
        internal DrawingInventory Inventory { get; set; } = inventory;
        internal int ReadCount { get; private set; }
        internal ImmutableArray<IPlacedObjectId> Targets { get; private set; } = [];

        public Task<HostResult<LensPresentation>> ReadAsync(
            DrawingGrouping grouping,
            IReadOnlyCollection<string> enabledFilters,
            ImmutableArray<IPlacedObjectId>? selectedObjects,
            CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult<HostResult<LensPresentation>>(
                new HostResult<LensPresentation>.Success(DrawingLensProvider.Build(Inventory, grouping, enabledFilters)));
        }

        public Task<HostResult<ImmutableArray<IPlacedObjectId>>> RequestObjectsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void ClearImmediately(bool hostTerminating) => Targets = [];

        public Task<HostResult<bool>> SelectAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken)
        {
            Targets = objects;
            return Success();
        }

        public Task<HostResult<bool>> ClearIsolationAsync(CancellationToken cancellationToken) => ClearAsync(cancellationToken);

        public Task<HostResult<bool>> ClearAsync(CancellationToken cancellationToken)
        {
            Targets = [];
            return Success();
        }

        public Task<string> IsolateObjectsAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken)
        {
            Targets = objects;
            return Task.FromResult("");
        }

        public Task<string> FocusAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken)
        {
            Targets = objects;
            return Task.FromResult("");
        }

        private static Task<HostResult<bool>> Success() =>
            Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(true));
    }
}