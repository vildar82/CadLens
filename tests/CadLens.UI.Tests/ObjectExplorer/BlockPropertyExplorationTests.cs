using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using CadLens.Common;
using CadLens.Lenses;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>Drawing-owned block properties retain distinct identities throughout the shared explorer.</summary>
[Collection("Language changes")]
public sealed class BlockPropertyExplorationTests : IDisposable
{
    private readonly LanguagePreference _previous = UiText.Current.Preference;
    private static readonly DrawingPropertyKey AttributeLayer = DrawingPropertyKey.ForAttribute("Layer");
    private static readonly DrawingPropertyKey DynamicLayer = DrawingPropertyKey.ForDynamicBlock("Layer");
    private static readonly DrawingPropertyKey DynamicLength = DrawingPropertyKey.ForDynamicBlock("Length");

    /// <summary>Both lenses expose distinct labels and sort numeric values without converting attribute text.</summary>
    [Theory]
    [InlineData(DrawingGrouping.Layers, LanguagePreference.English)]
    [InlineData(DrawingGrouping.ObjectTypes, LanguagePreference.English)]
    [InlineData(DrawingGrouping.Layers, LanguagePreference.Russian)]
    [InlineData(DrawingGrouping.ObjectTypes, LanguagePreference.Russian)]
    public async Task SameNamedPropertiesStayDistinct(DrawingGrouping grouping, LanguagePreference language)
    {
        UiText.Current.Select(language, persist: false);
        var actions = new PropertyFilterTests.Actions {InventoryOverride = Inventory()};
        using var model = new ObjectExplorerViewModel(actions, grouping);
        await EnterType(model, grouping);
        var labels = model.DisplayPropertyOptions.ToDictionary(option => option.Id, option => option.Label);
        Assert.Equal(language == LanguagePreference.English ? "Layer" : "Слой", labels[DrawingPropertyId.Layer]);
        Assert.Equal(language == LanguagePreference.English ? "Attribute: Layer" : "Атрибут: Layer", labels[AttributeLayer]);
        Assert.Equal(language == LanguagePreference.English ? "Dynamic block: Layer" : "Дин. свойство: Layer", labels[DynamicLayer]);
        Assert.Equal(3, new[] {labels[DrawingPropertyId.Layer], labels[AttributeLayer], labels[DynamicLayer]}.Distinct().Count());

        Show(model, AttributeLayer);
        Assert.Equal(["3", "2", "1", "4", "5"], model.Items.Select(node => node.Id));
        Assert.Equal("002", DrawingValueFormatter.FormatMetric(model.Items[1], AttributeLayer));
        Show(model, DynamicLength);
        Assert.Equal(["3", "2", "1", "4", "5"], model.Items.Select(node => node.Id));
        Assert.Equal("2", DrawingValueFormatter.FormatMetric(model.Items[1], DynamicLength));

        model.PropertyFilter.PropertyId = AttributeLayer;
        model.PropertyFilter.InputText = "002";
        await model.ApplyPropertyFilterCommand.ExecuteAsync(null);
        Assert.Equal(["2"], model.Current!.Objects.Select(id => id.DisplayId));
        Assert.Equal(new DrawingTextValue("002"), model.AppliedPropertyFilter!.Value);
        await model.ToggleGroupingCommand.ExecuteAsync(model.GroupingOptions.Single(option => option.Id == DynamicLayer));
        Assert.Contains(labels[DynamicLayer], DrawingValueFormatter.FormatLabel(Assert.Single(model.Items)));
        await model.SelectCommand.ExecuteAsync(null);
        Assert.Equal(["2"], actions.Selected.Select(id => id.DisplayId));
        Assert.Equal(1, actions.ReadCount);
    }

    /// <summary>Language changes preserve the chosen source and exact filter draft.</summary>
    [Fact]
    public async Task LanguageChangesReorderNamesWithoutChangingSelections()
    {
        UiText.Current.Select(LanguagePreference.English, persist: false);
        using var model = new ObjectExplorerViewModel(
            new PropertyFilterTests.Actions {InventoryOverride = Inventory()},
            DrawingGrouping.ObjectTypes);
        await EnterType(model, DrawingGrouping.ObjectTypes);
        Show(model, DynamicLayer);
        model.PropertyFilter.PropertyId = AttributeLayer;
        model.PropertyFilter.Operator = DrawingFilterOperator.Contains;
        model.PropertyFilter.InputText = "00";
        UiText.Current.Select(LanguagePreference.Russian, persist: false);

        Assert.Equal(DynamicLayer, model.DisplayPropertyId);
        Assert.Equal(AttributeLayer, model.PropertyFilter.PropertyId);
        Assert.Equal(DrawingFilterOperator.Contains, model.PropertyFilter.Operator);
        Assert.Equal("00", model.PropertyFilter.InputText);
        Assert.Equal("Дин. свойство: Layer", model.DisplayPropertyLabel);
        Assert.Contains(model.PropertyFilter.PropertyOptions, option => option.Id == AttributeLayer && option.Label == "Атрибут: Layer");
        var labels = model.DisplayPropertyOptions.Select(option => option.Label).ToArray();
        Assert.Equal(labels.OrderBy(label => label, StringComparer.Create(UiText.Current.Culture, true)), labels);
    }

    /// <summary>Display/group choices and saved conditions restore names without persisting drawing IDs.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NamedPropertiesSurviveRestart(bool dynamic)
    {
        UiText.Current.Select(LanguagePreference.English, persist: false);
        using var settings = new SettingsFile();
        var key = dynamic ? DynamicLength : AttributeLayer;
        var inventory = Inventory();
        using (var first = new ObjectExplorerViewModel(
                   new PropertyFilterTests.Actions {InventoryOverride = inventory},
                   DrawingGrouping.ObjectTypes,
                   settings.Service))
        {
            await EnterType(first, DrawingGrouping.ObjectTypes);
            Show(first, key);
            await first.ToggleGroupingCommand.ExecuteAsync(first.GroupingOptions.Single(option => option.Id == key));
            first.PropertyFilter.PropertyId = key;
            first.PropertyFilter.InputText = dynamic ? "2" : "002";
            first.SavedFilterName = "Named block property";
            first.SavePropertyFilterCommand.Execute(null);
            Assert.Single(first.SavedFilterNames);
        }

        UiText.Current.Select(LanguagePreference.Russian, persist: false);
        using var restored = new ObjectExplorerViewModel(
            new PropertyFilterTests.Actions {InventoryOverride = inventory},
            DrawingGrouping.ObjectTypes,
            settings.Service);
        await EnterType(restored, DrawingGrouping.ObjectTypes);
        Assert.Equal(key, restored.DisplayPropertyId);
        Assert.True(restored.GroupingOptions.Single(option => option.Id == key).IsSelected);
        restored.SelectedSavedFilter = Assert.Single(restored.SavedFilterNames);
        await restored.ApplySavedPropertyFilterCommand.ExecuteAsync(null);
        Assert.Equal(key, restored.AppliedPropertyFilter!.PropertyId);
        Assert.Equal(["2"], restored.Current!.Objects.Select(id => id.DisplayId));
#if NETFRAMEWORK
        var json = File.ReadAllText(Path.Combine(settings.Directory, "property-filters.json"));
#else
        var json = await File.ReadAllTextAsync(Path.Combine(settings.Directory, "property-filters.json"));
#endif
        Assert.Contains(key.ToString(), json);
        Assert.DoesNotContain("source-layer-id", json);
    }

    /// <summary>Previously written numeric enum IDs remain valid after named property keys are introduced.</summary>
    [Fact]
    public async Task LegacyNumericPresetStillApplies()
    {
        using var settings = new SettingsFile();
        var json = JsonSerializer.Serialize(new[]
        {
            new
            {
                Name = "Legacy",
                TypeKey = "AcDbPolyline",
                PropertyId = (int) DrawingPropertyId.Length,
                Operator = DrawingFilterOperator.GreaterThan,
                ValueKind = "Distance",
                Value = 5
            }
        });
#if NETFRAMEWORK
        File.WriteAllText(Path.Combine(settings.Directory, "property-filters.json"), json);
#else
        await File.WriteAllTextAsync(Path.Combine(settings.Directory, "property-filters.json"), json);
#endif
        using var model = new ObjectExplorerViewModel(new PropertyFilterTests.Actions(), DrawingGrouping.ObjectTypes, settings.Service);
        await EnterType(model, DrawingGrouping.ObjectTypes);
        model.SelectedSavedFilter = "Legacy";
        await model.ApplySavedPropertyFilterCommand.ExecuteAsync(null);
        Assert.Equal(DrawingPropertyId.Length, model.AppliedPropertyFilter!.PropertyId);
        Assert.Equal(["2", "3"], model.Current!.Objects.Select(id => id.DisplayId));
    }

    /// <summary>Same-named dynamic values with different native types remain selectable without ambiguous captions.</summary>
    [Fact]
    public async Task MixedDynamicValuesUseDistinctTypedChoices()
    {
        UiText.Current.Select(LanguagePreference.English, persist: false);
        var key = DrawingPropertyKey.ForDynamicBlock("Mixed");
        var values = new DrawingValue[]
        {
            new DrawingTextValue("2"),
            new DrawingNumberValue(2, DrawingUnit.Distance),
            new DrawingNumberValue(2, DrawingUnit.Scale),
            new DrawingNumberValue(3, DrawingUnit.Distance)
        };
        var inventory = Inventory();
        inventory = inventory with
        {
            Entities = [.. inventory.Entities.Take(4).Select((entity, index) => entity with
            {
                DynamicBlockProperties = [new DynamicBlockPropertySnapshot("Mixed", values[index])]
            })]
        };
        using var model = new ObjectExplorerViewModel(
            new PropertyFilterTests.Actions {InventoryOverride = inventory},
            DrawingGrouping.ObjectTypes);
        await EnterType(model, DrawingGrouping.ObjectTypes);
        model.PropertyFilter.PropertyId = key;
        Assert.True(model.PropertyFilter.UsesValueOptions);
        Assert.Equal(4, model.PropertyFilter.ValueOptions.Select(option => option.Label).Distinct().Count());
        model.PropertyFilter.SelectedValue = values[1];
        await model.ApplyPropertyFilterCommand.ExecuteAsync(null);
        Assert.Equal(["2"], model.Current!.Objects.Select(id => id.DisplayId));
        model.PropertyFilter.SelectedValue = values[3];
        await model.ApplyPropertyFilterCommand.ExecuteAsync(null);
        Assert.Equal(values[3], model.AppliedPropertyFilter!.Value);
        Assert.Equal(["4"], model.Current!.Objects.Select(id => id.DisplayId));
    }

    /// <inheritdoc />
    public void Dispose() => UiText.Current.Select(_previous, persist: false);

    private static void Show(ObjectExplorerViewModel model, DrawingPropertyKey key) =>
        model.SelectDisplayPropertyCommand.Execute(model.DisplayPropertyOptions.Single(option => option.Id == key));

    private static async Task EnterType(ObjectExplorerViewModel model, DrawingGrouping grouping)
    {
        await model.ActivateAsync(CancellationToken.None);
        if (grouping == DrawingGrouping.Layers)
            await model.EnterCommand.ExecuteAsync(Assert.Single(model.Items));

        await model.EnterCommand.ExecuteAsync(Assert.Single(model.Items));
    }

    private static DrawingInventory Inventory()
    {
        var layer = new LayerId("source-layer-id");
        var properties = ImmutableDictionary<DrawingPropertyId, DrawingValue?>.Empty
            .Add(DrawingPropertyId.BlockName, new DrawingTextValue("Door"))
            .Add(DrawingPropertyId.Attributes, new DrawingNumberValue(1, DrawingUnit.Count))
            .Add(DrawingPropertyId.Dynamic, new DrawingBooleanValue(true));
        var entities = new List<EntitySnapshot>();
        string[] text = ["010", "002", ""];
        double[] numbers = [10, 2, 0];
        for (var index = 0; index < 5; index++)
        {
            entities.Add(new EntitySnapshot(
                new ObjectId((index + 1).ToString()),
                layer,
                "AcDbBlockReference",
                properties,
                BlockAttributes: index < 3 ? [new BlockAttributeSnapshot("Layer", text[index])] : index == 4
                    ? [new BlockAttributeSnapshot("Layer", "X"), new BlockAttributeSnapshot("Layer", "Y")] : [],
                DynamicBlockProperties: index < 3
                    ? [new DynamicBlockPropertySnapshot("Layer", new DrawingTextValue(index == 0 ? "Open" : "Closed")),
                        new DynamicBlockPropertySnapshot("Length", new DrawingNumberValue(numbers[index], DrawingUnit.Distance))]
                    : []));
        }

        return new DrawingInventory("Model", [new LayerSnapshot(layer, "Floor", false, false, false, false)], [.. entities]);
    }

    private sealed record LayerId(string DisplayId) : ILayerId;
    private sealed record ObjectId(string DisplayId) : IPlacedObjectId;
}
