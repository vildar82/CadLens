using System.Collections.Immutable;
using System.IO;
using CadLens.Common;
using CadLens.Lenses;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>Named conditions keep exact values and remain independent of drawing identities.</summary>
[Collection("Language changes")]
public sealed class SavedPropertyFilterTests
{
    /// <summary>Saving shares a condition across lenses and sessions; deletion preserves the current result.</summary>
    [Fact]
    public async Task SaveApplyAndDeleteUseSharedSettingsWithoutReadingCad()
    {
        using var language = new LanguageScope(LanguagePreference.English);
        using var settings = new SettingsFile();
        var actions = new PropertyFilterTests.Actions();
        using var first = new ObjectExplorerViewModel(actions, DrawingGrouping.ObjectTypes, settings.Service);
        await EnterType(first);
        first.PropertyFilter.PropertyId = DrawingPropertyId.Length;
        first.PropertyFilter.Operator = DrawingFilterOperator.GreaterThan;
        first.PropertyFilter.InputText = "5.123456789012345";
        first.SavedFilterName = "Длинные полилинии";
        first.SavePropertyFilterCommand.Execute(null);
        Assert.Null(first.AppliedPropertyFilter);
        Assert.Equal(4, first.ObjectCount);
        Assert.Equal(first.SavedFilterName, Assert.Single(first.SavedFilterNames));
        Assert.Equal(1, actions.ReadCount);

        var otherActions = new PropertyFilterTests.Actions();
        using var second = new ObjectExplorerViewModel(otherActions, DrawingGrouping.Layers, settings.Service);
        await second.ActivateAsync(CancellationToken.None);
        await second.EnterCommand.ExecuteAsync(Assert.Single(second.Items));
        await second.EnterCommand.ExecuteAsync(Assert.Single(second.Items));
        second.SelectedSavedFilter = Assert.Single(second.SavedFilterNames);
        await second.ApplySavedPropertyFilterCommand.ExecuteAsync(null);
        Assert.Equal(5.123456789012345, Assert.IsType<DrawingNumberValue>(second.AppliedPropertyFilter!.Value).Value);
        Assert.Equal(["2", "3"], second.Current!.Objects.Select(id => id.DisplayId));
        Assert.Equal(1, otherActions.ReadCount);
        second.DeleteSavedPropertyFilterCommand.Execute(null);
        Assert.Empty(second.SavedFilterNames);
        Assert.Equal(["2", "3"], second.Current.Objects.Select(id => id.DisplayId));
        Assert.NotNull(second.AppliedPropertyFilter);
        first.RefreshSavedPropertyFiltersCommand.Execute(null);
        Assert.Empty(first.SavedFilterNames);
        using var restored = new ObjectExplorerViewModel(new PropertyFilterTests.Actions(), DrawingGrouping.ObjectTypes, settings.Service);
        await EnterType(restored);
        Assert.Empty(restored.SavedFilterNames);
    }

    /// <summary>Typed settings round-trip without localized captions or precision loss.</summary>
    [Theory]
    [InlineData(DrawingPropertyId.Length)]
    [InlineData(DrawingPropertyId.Area)]
    [InlineData(DrawingPropertyId.StartAngle)]
    [InlineData(DrawingPropertyId.Closed)]
    [InlineData(DrawingPropertyId.Linetype)]
    [InlineData(DrawingPropertyId.Color)]
    [InlineData(DrawingPropertyId.Lineweight)]
    [InlineData(DrawingPropertyId.Transparency)]
    [InlineData(DrawingPropertyId.Text)]
    public async Task ExactTypedValuesSurviveRestartAndLanguageChange(DrawingPropertyId property)
    {
        using var language = new LanguageScope(LanguagePreference.English);
        using var settings = new SettingsFile();
        var value = ValueFor(property);
        var inventory = Inventory(property, value);
        DrawingValue expected;
        using (var first = new ObjectExplorerViewModel(
                   new PropertyFilterTests.Actions {InventoryOverride = inventory},
                   DrawingGrouping.ObjectTypes,
                   settings.Service))
        {
            await EnterType(first);
            first.PropertyFilter.PropertyId = property;
            if (first.PropertyFilter.UsesValueOptions)
                first.PropertyFilter.SelectedValue = value;
            else
                first.PropertyFilter.InputText = property == DrawingPropertyId.StartAngle ? "17.123456789012345" :
                    value is DrawingNumberValue ? "1.123456789012345" : "Труба\nA";

            await first.ApplyPropertyFilterCommand.ExecuteAsync(null);
            expected = first.AppliedPropertyFilter!.Value;
            first.SavedFilterName = "Exact";
            first.SavePropertyFilterCommand.Execute(null);
        }

        UiText.Current.Select(LanguagePreference.Russian, persist: false);
        var actions = new PropertyFilterTests.Actions {InventoryOverride = inventory};
        using var restored = new ObjectExplorerViewModel(actions, DrawingGrouping.ObjectTypes, settings.Service);
        await EnterType(restored);
        Assert.Null(restored.AppliedPropertyFilter);
        restored.SelectedSavedFilter = Assert.Single(restored.SavedFilterNames);
        await restored.ApplySavedPropertyFilterCommand.ExecuteAsync(null);
        Assert.Equal(expected, restored.AppliedPropertyFilter!.Value);
        Assert.Equal(1, actions.ReadCount);
    }

    /// <summary>Layer conditions store names and bind to the current drawing's layer identity.</summary>
    [Fact]
    public async Task LayerPresetResolvesNameWithoutPersistingDrawingIds()
    {
        using var settings = new SettingsFile();
        var original = Inventory(DrawingPropertyId.Length, new DrawingNumberValue(1, DrawingUnit.Distance));
        using (var first = new ObjectExplorerViewModel(
                   new PropertyFilterTests.Actions {InventoryOverride = original},
                   DrawingGrouping.ObjectTypes,
                   settings.Service))
        {
            await EnterType(first);
            first.PropertyFilter.PropertyId = DrawingPropertyId.Layer;
            first.SavedFilterName = "Road layer";
            first.SavePropertyFilterCommand.Execute(null);
        }

#if NETFRAMEWORK
        var json = File.ReadAllText(Path.Combine(settings.Directory, "property-filters.json"));
#else
        var json = await File.ReadAllTextAsync(Path.Combine(settings.Directory, "property-filters.json"));
#endif
        Assert.DoesNotContain("secret-layer-id", json);
        Assert.DoesNotContain("secret-object-id", json);
        var otherLayer = new LayerId("different-layer-id");
        var current = original with
        {
            Layers = [original.Layers[0] with {Id = otherLayer}],
            Entities = [original.Entities[0] with {Id = new ObjectId("different-object-id"), LayerId = otherLayer}]
        };
        using var restored = new ObjectExplorerViewModel(
            new PropertyFilterTests.Actions {InventoryOverride = current},
            DrawingGrouping.ObjectTypes,
            settings.Service);
        await EnterType(restored);
        restored.SelectedSavedFilter = Assert.Single(restored.SavedFilterNames);
        await restored.ApplySavedPropertyFilterCommand.ExecuteAsync(null);
        Assert.Equal(otherLayer, Assert.IsType<DrawingLayerValue>(restored.AppliedPropertyFilter!.Value).Id);
        Assert.Equal(1, restored.ObjectCount);
    }

    /// <summary>Unavailable properties preserve the valid result and drawing effects.</summary>
    [Fact]
    public async Task UnavailablePresetLeavesAppliedFilterAndTargetsUnchanged()
    {
        using var language = new LanguageScope(LanguagePreference.English);
        using var settings = new SettingsFile();
        using (var first = new ObjectExplorerViewModel(new PropertyFilterTests.Actions(), DrawingGrouping.ObjectTypes, settings.Service))
        {
            await EnterType(first);
            first.PropertyFilter.PropertyId = DrawingPropertyId.StartAngle;
            first.PropertyFilter.InputText = "90";
            first.SavedFilterName = "Angle";
            first.SavePropertyFilterCommand.Execute(null);
        }

        var actions = new PropertyFilterTests.Actions
        {
            InventoryOverride = Inventory(DrawingPropertyId.Length, new DrawingNumberValue(1, DrawingUnit.Distance))
        };
        using var second = new ObjectExplorerViewModel(actions, DrawingGrouping.ObjectTypes, settings.Service);
        await EnterType(second);
        second.PropertyFilter.PropertyId = DrawingPropertyId.Length;
        second.PropertyFilter.InputText = "1";
        await second.ApplyPropertyFilterCommand.ExecuteAsync(null);
        await second.IsolateCommand.ExecuteAsync(null);
        var previous = second.AppliedPropertyFilter;
        var effects = actions.EffectCalls;
        second.SelectedSavedFilter = Assert.Single(second.SavedFilterNames);
        await second.ApplySavedPropertyFilterCommand.ExecuteAsync(null);
        Assert.Equal(previous, second.AppliedPropertyFilter);
        Assert.Equal(1, second.ObjectCount);
        Assert.Equal(effects, actions.EffectCalls);
        Assert.Contains("not available", second.SavedFilterMessage);
        Assert.Equal(1, actions.ReadCount);
    }

    /// <summary>Malformed settings recover; failed writes keep the explorer usable and report failure.</summary>
    [Fact]
    [System.ComponentModel.Localizable(false)]
    public async Task InvalidOrUnwritableSettingsDoNotBreakTheExplorer()
    {
        using var language = new LanguageScope(LanguagePreference.English);
        using var settings = new SettingsFile();
        var path = Path.Combine(settings.Directory, "property-filters.json");
#if NETFRAMEWORK
        File.WriteAllText(path, "{");
#else
        await File.WriteAllTextAsync(path, "{");
#endif
        using var model = new ObjectExplorerViewModel(new PropertyFilterTests.Actions(), DrawingGrouping.ObjectTypes, settings.Service);
        await EnterType(model);
        Assert.Empty(model.SavedFilterNames);
        model.PropertyFilter.PropertyId = DrawingPropertyId.Length;
        model.PropertyFilter.InputText = "5";
        model.SavedFilterName = "Saved";
        model.SavePropertyFilterCommand.Execute(null);
        Assert.Equal("Saved", Assert.Single(model.SavedFilterNames));
        model.SavePropertyFilterCommand.Execute(null);
        Assert.Single(model.SavedFilterNames);
        Assert.Contains("already exists", model.SavedFilterMessage);
        File.Delete(path);
        Directory.CreateDirectory(path);
        model.SavedFilterName = "Failed";
        model.SavePropertyFilterCommand.Execute(null);
        Assert.Contains("could not be written", model.SavedFilterMessage);
        Assert.Equal(4, model.ObjectCount);
        Assert.Null(model.AppliedPropertyFilter);
    }

    /// <summary>Corrupt typed payloads cannot replace a valid result or clear its effects.</summary>
    [Theory]
    [InlineData("Text", "\"5\"")]
    [InlineData("Scale", "5")]
    [InlineData("Distance", "1e400")]
    public async Task IncompatibleStoredValuesKeepTheCurrentResult(string kind, string valueJson)
    {
        using var settings = new SettingsFile();
        var json = System.Text.Json.JsonSerializer.Serialize(new[]
        {
            new
            {
                Name = "Corrupt",
                TypeKey = "AcDbPolyline",
                PropertyId = DrawingPropertyId.Length,
                Operator = DrawingFilterOperator.Equal,
                ValueKind = kind,
                Value = "PAYLOAD"
            }
        }).Replace("\"PAYLOAD\"", valueJson);
#if NETFRAMEWORK
        File.WriteAllText(Path.Combine(settings.Directory, "property-filters.json"), json);
#else
        await File.WriteAllTextAsync(Path.Combine(settings.Directory, "property-filters.json"), json);
#endif
        var actions = new PropertyFilterTests.Actions();
        using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.ObjectTypes, settings.Service);
        await EnterType(model);
        model.PropertyFilter.PropertyId = DrawingPropertyId.Length;
        model.PropertyFilter.Operator = DrawingFilterOperator.GreaterThan;
        model.PropertyFilter.InputText = "5";
        await model.ApplyPropertyFilterCommand.ExecuteAsync(null);
        await model.IsolateCommand.ExecuteAsync(null);
        var previous = model.AppliedPropertyFilter;
        var effects = actions.EffectCalls;
        model.SelectedSavedFilter = "Corrupt";
        await model.ApplySavedPropertyFilterCommand.ExecuteAsync(null);
        Assert.Equal(previous, model.AppliedPropertyFilter);
        Assert.Equal(2, model.ObjectCount);
        Assert.Equal(effects, actions.EffectCalls);
        Assert.NotEmpty(model.SavedFilterMessage);
    }

    private static async Task EnterType(ObjectExplorerViewModel model)
    {
        await model.ActivateAsync(CancellationToken.None);
        await model.EnterCommand.ExecuteAsync(Assert.Single(model.Items));
    }

    private static DrawingValue ValueFor(DrawingPropertyId property) => property switch
    {
        DrawingPropertyId.Length or DrawingPropertyId.Area => new DrawingNumberValue(
            1.123456789012345,
            property == DrawingPropertyId.Area ? DrawingUnit.Area : DrawingUnit.Distance),
        DrawingPropertyId.StartAngle => new DrawingNumberValue(17.123456789012345 * Math.PI / 180, DrawingUnit.Angle),
        DrawingPropertyId.Closed => new DrawingBooleanValue(true),
        DrawingPropertyId.Linetype => new DrawingTextValue("ByLayer", true),
        DrawingPropertyId.Color => new DrawingColorValue(new AssignedColor(AssignedColorKind.ColorBook, 23, "Name", "Book")),
        DrawingPropertyId.Lineweight => new DrawingLineweightValue(new AssignedLineweight(AssignedLineweightKind.Explicit, 35)),
        DrawingPropertyId.Transparency => new DrawingTransparencyValue(new AssignedTransparency(AssignedTransparencyKind.Explicit, 127)),
        _ => new DrawingTextValue("Труба\nA")
    };

    private static DrawingInventory Inventory(DrawingPropertyId property, DrawingValue value)
    {
        var layer = new LayerId("secret-layer-id");
        return new DrawingInventory(
            "Model",
            [new LayerSnapshot(layer, "Roads", false, false, false, false)],
            [new EntitySnapshot(new ObjectId("secret-object-id"), layer, "AcDbPolyline",
                new Dictionary<DrawingPropertyId, DrawingValue?> { [property] = value }.ToImmutableDictionary())]);
    }

    private sealed class LanguageScope : IDisposable
    {
        private readonly LanguagePreference _previous = UiText.Current.Preference;

        internal LanguageScope(LanguagePreference preference) => UiText.Current.Select(preference, persist: false);

        public void Dispose() => UiText.Current.Select(_previous, persist: false);
    }

    private sealed record LayerId(string DisplayId) : ILayerId;
    private sealed record ObjectId(string DisplayId) : IPlacedObjectId;
}
