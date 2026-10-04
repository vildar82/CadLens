using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using CadLens.Lenses;
using CadLens.Common;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>Persistent lens controls stay independent from drawing work and transient navigation.</summary>
[Collection("WPF")]
public sealed class LensSettingsTests
{
    /// <summary>Reopening restores every control before activation and runs Auto modes only while browsing.</summary>
    [Theory]
    [InlineData(DrawingGrouping.Layers)]
    [InlineData(DrawingGrouping.ObjectTypes)]
    public async Task ReopenRestoresControlsBeforeAnyDrawingWork(DrawingGrouping grouping)
    {
        using var file = new SettingsFile();
        using var original = new ObjectExplorerViewModel(new Actions(), grouping, file.Service);
        await ConfigureAsync(original);
        original.SearchText = "be";
        original.SortByCountCommand.Execute(null);
        original.SortByCountCommand.Execute(null);
        await original.EnterCommand.ExecuteAsync(Assert.Single(original.Items));
        original.Close(false);

        var actions = new Actions();
        using var reopened = new ObjectExplorerViewModel(actions, grouping, new SettingsService(file.Directory));
        Assert.Equal(0, actions.NativeCalls);
        Assert.False(reopened.IsLensActive);
        Assert.Null(reopened.Current);
        Assert.Empty(reopened.Breadcrumbs);
        Assert.Empty(reopened.Items);
        Assert.Equal("be", reopened.SearchText);
        Assert.True(reopened.IsCountSortActive);
        Assert.Equal("↑", reopened.CountSortArrow);
        Assert.True(reopened is {IsAutoFocus: true, IsAutoSelect: true, IsAutoIsolation: true});

        await reopened.ActivateAsync(CancellationToken.None);
        Assert.Equal("custom-filter", Assert.Single(Assert.Single(actions.Reads)));
        Assert.True(reopened.Filters[0].IsEnabled);
        Assert.Equal((0, 0, 0), (actions.FocusCount, actions.SelectCount, actions.IsolateCount));
        await reopened.EnterCommand.ExecuteAsync(Assert.Single(reopened.Items));
        Assert.Equal((1, 1, 1), (actions.FocusCount, actions.SelectCount, actions.IsolateCount));
        var saved = File.ReadAllText(SettingsPath(file, grouping));
        await reopened.DeactivateAsync(CancellationToken.None);
        reopened.Close(false);
        Assert.Equal(saved, File.ReadAllText(SettingsPath(file, grouping)));
    }

    /// <summary>Each grouping reopens its own filter, search, sorting, and Auto settings.</summary>
    [Fact]
    public async Task GroupingsPersistIndependentControls()
    {
        using var file = new SettingsFile();
        using var layers = new ObjectExplorerViewModel(new Actions(), DrawingGrouping.Layers, file.Service);
        using var types = new ObjectExplorerViewModel(new Actions(), DrawingGrouping.ObjectTypes, file.Service);
        await layers.ActivateAsync(CancellationToken.None);
        await types.ActivateAsync(CancellationToken.None);
        await layers.ToggleFilterCommand.ExecuteAsync(layers.Filters[0]);
        await layers.ToggleAutoFocusCommand.ExecuteAsync(null);
        layers.SearchText = "Alpha";
        layers.SortByNameCommand.Execute(null);
        await types.ToggleFilterCommand.ExecuteAsync(types.Filters[1]);
        await types.ToggleAutoSelectCommand.ExecuteAsync(null);
        types.SearchText = "Beta";
        types.SortByCountCommand.Execute(null);

        using var reopenedLayers = new ObjectExplorerViewModel(new Actions(), DrawingGrouping.Layers, file.Service);
        using var reopenedTypes = new ObjectExplorerViewModel(new Actions(), DrawingGrouping.ObjectTypes, file.Service);
        Assert.Equal("Alpha", reopenedLayers.SearchText);
        Assert.Equal("Beta", reopenedTypes.SearchText);
        Assert.Equal("↓", reopenedLayers.NameSortArrow);
        Assert.Equal("↓", reopenedTypes.CountSortArrow);
        Assert.True(reopenedLayers.IsAutoFocus);
        Assert.False(reopenedLayers.IsAutoSelect || reopenedLayers.IsAutoIsolation);
        Assert.True(reopenedTypes.IsAutoSelect);
        Assert.False(reopenedTypes.IsAutoFocus || reopenedTypes.IsAutoIsolation);
        await reopenedLayers.ActivateAsync(CancellationToken.None);
        await reopenedTypes.ActivateAsync(CancellationToken.None);
        Assert.Equal([true, false], reopenedLayers.Filters.Select(filter => filter.IsEnabled));
        Assert.Equal([false, true], reopenedTypes.Filters.Select(filter => filter.IsEnabled));
    }

    /// <summary>Reset saves Auto modes off while preserving filter, search, and sort preferences.</summary>
    [Fact]
    public async Task ResetPersistsOnlyTheControlsItChanges()
    {
        using var file = new SettingsFile();
        using var model = new ObjectExplorerViewModel(new Actions(), DrawingGrouping.Layers, file.Service);
        await ConfigureAsync(model);
        model.SearchText = "Alpha";
        model.SortByCountCommand.Execute(null);
        await model.ResetCommand.ExecuteAsync(null);

        using var reopened = new ObjectExplorerViewModel(new Actions(), DrawingGrouping.Layers, file.Service);
        Assert.False(reopened.IsAutoFocus || reopened.IsAutoSelect || reopened.IsAutoIsolation);
        Assert.Equal("Alpha", reopened.SearchText);
        Assert.True(reopened.IsCountSortActive);
        Assert.Equal("↓", reopened.CountSortArrow);
        await reopened.ActivateAsync(CancellationToken.None);
        Assert.True(reopened.Filters[0].IsEnabled);
    }

    /// <summary>Invalid JSON and null controls cannot break the default inactive lens.</summary>
    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{\"IsAutoFocus\":\"invalid\"}")]
    [InlineData("{\"EnabledFilters\":null,\"SearchText\":null}")]
    public async Task InvalidSettingsUseSensibleDefaults(string json)
    {
        using var file = new SettingsFile();
        File.WriteAllText(SettingsPath(file, DrawingGrouping.Layers), json);
        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.Layers, file.Service);
        Assert.Equal(0, actions.NativeCalls);
        Assert.False(model.IsLensActive || model.IsAutoFocus || model.IsAutoSelect || model.IsAutoIsolation);
        Assert.Equal("", model.SearchText);
        Assert.False(model.IsCountSortActive);
        Assert.Equal("↑", model.NameSortArrow);
        await model.ActivateAsync(CancellationToken.None);
        Assert.Empty(Assert.Single(actions.Reads));
        Assert.All(model.Filters, filter => Assert.False(filter.IsEnabled));
    }

    /// <summary>The first read receives saved generic IDs; later reads drop descriptors no longer available.</summary>
    [Fact]
    public async Task RestoredFiltersReconcileAfterTheFirstInventory()
    {
        using var file = new SettingsFile();
        var path = SettingsPath(file, DrawingGrouping.Layers);
        const string json = "{\"EnabledFilters\":[\"custom-filter\",\"removed-filter\",null,\"\"]}";
        File.WriteAllText(path, json);
        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.Layers, file.Service);
        Assert.Equal(json, File.ReadAllText(path));
        await model.ActivateAsync(CancellationToken.None);
        Assert.Equal(["custom-filter", "removed-filter"], actions.Reads[0].OrderBy(item => item, StringComparer.Ordinal));
        Assert.True(model.Filters[0].IsEnabled);
        await model.ReadCommand.ExecuteAsync(null);
        Assert.Equal("custom-filter", Assert.Single(actions.Reads[1]));
        model.SearchText = "Beta";
        using var saved = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(
            "custom-filter",
            Assert.Single(saved.RootElement.GetProperty("EnabledFilters").EnumerateArray())
                .GetString());
    }

    /// <summary>
    /// Failed writes retain working controls, report session-only changes, and retry on the next change.
    /// </summary>
    [Fact]
    public async Task FailedSaveKeepsTheSessionUsableAndCanRecover()
    {
        using var file = new SettingsFile();
        var blocked = SettingsPath(file, DrawingGrouping.Layers);
        Directory.CreateDirectory(blocked);
        using var model = new ObjectExplorerViewModel(new Actions(), DrawingGrouping.Layers, file.Service);
        await ConfigureAsync(model);
        model.SearchText = "Beta";
        model.SortByCountCommand.Execute(null);
        Assert.True(model is {IsAutoFocus: true, IsAutoSelect: true, IsAutoIsolation: true});
        Assert.True(model.Filters[0].IsEnabled);
        Assert.Equal("Beta", Assert.Single(model.Items).Label);
        Assert.Contains("Unable to save preferences. Changes apply for this session.", model.Status);
        await model.EnterCommand.ExecuteAsync(Assert.Single(model.Items));
        Assert.NotNull(model.Current);
        Directory.Delete(blocked);
        model.SortByNameCommand.Execute(null);
        Assert.DoesNotContain("Unable to save preferences.", model.Status);
        using var reopened = new ObjectExplorerViewModel(new Actions(), DrawingGrouping.Layers, file.Service);
        Assert.Equal("Beta", reopened.SearchText);
        Assert.True(reopened is {IsAutoFocus: true, IsAutoSelect: true, IsAutoIsolation: true});
        await reopened.ActivateAsync(CancellationToken.None);
        Assert.True(reopened.Filters[0].IsEnabled);
    }

    /// <summary>Commands held by a closed lens cannot overwrite controls saved by the current session.</summary>
    [Fact]
    public async Task DisposedLensCannotOverwriteCurrentPreferences()
    {
        using var file = new SettingsFile();
        var closed = new ObjectExplorerViewModel(new Actions(), DrawingGrouping.Layers, file.Service);
        closed.Dispose();
        using var current = new ObjectExplorerViewModel(new Actions(), DrawingGrouping.Layers, file.Service);
        current.SearchText = "new";
        var path = SettingsPath(file, DrawingGrouping.Layers);
        var saved = File.ReadAllText(path);
        closed.SearchText = "stale";
        closed.SortByCountCommand.Execute(null);
        closed.SortByNameCommand.Execute(null);
        closed.ClearSearchCommand.Execute(null);
        Assert.Equal(saved, File.ReadAllText(path));
        using var reopened = new ObjectExplorerViewModel(new Actions(), DrawingGrouping.Layers, file.Service);
        Assert.Equal("new", reopened.SearchText);
        Assert.False(reopened.IsCountSortActive);
        Assert.Equal("↑", reopened.NameSortArrow);
    }

    private static async Task ConfigureAsync(ObjectExplorerViewModel model)
    {
        await model.ActivateAsync(CancellationToken.None);
        await model.ToggleFilterCommand.ExecuteAsync(model.Filters[0]);
        await model.ToggleAutoFocusCommand.ExecuteAsync(null);
        await model.ToggleAutoSelectCommand.ExecuteAsync(null);
        await model.ToggleAutoIsolationCommand.ExecuteAsync(null);
    }

    private static string SettingsPath(SettingsFile file, DrawingGrouping grouping) => Path.Combine(
        file.Directory,
        grouping == DrawingGrouping.Layers ? "lens-layers.json" : "lens-object-types.json");

    private sealed class Actions : IObjectExplorerActions
    {
        internal int NativeCalls { get; private set; }
        internal int FocusCount { get; private set; }
        internal int SelectCount { get; private set; }
        internal int IsolateCount { get; private set; }
        internal List<ImmutableHashSet<string>> Reads { get; } = [];

        public Task<HostResult<ImmutableArray<IPlacedObjectId>>> RequestObjectsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<HostResult<ImmutableArray<IPlacedObjectId>>>(new HostResult<ImmutableArray<IPlacedObjectId>>.Success([]));

        public void ClearImmediately(bool hostTerminating) => NativeCalls++;

        public Task<HostResult<LensPresentation>> ReadAsync(
            DrawingGrouping grouping,
            IReadOnlyCollection<string> enabledFilters,
            ImmutableArray<IPlacedObjectId>? selectedObjects,
            CancellationToken cancellationToken)
        {
            NativeCalls++;
            Reads.Add([.. enabledFilters]);
            var alpha = new LensNode("alpha", "Alpha", [new TestEntityId(1)], [], [], [LensAction.Focus]);
            var beta = alpha with {Id = "beta", Label = "Beta", Objects = [new TestEntityId(2), new TestEntityId(3)]};
            var presentation = new LensPresentation(
                "Fixture",
                "Test space",
                [alpha, beta],
                [
                    new BooleanFilter("custom-filter", "Custom", "Custom option", IconRole.Snowflake),
                    new BooleanFilter("extra-filter", "Extra", "Extra option", IconRole.Lightbulb)
                ],
                "Empty fixture",
                "All fixture groups",
                "Search fixture groups",
                "groups");
            return Task.FromResult<HostResult<LensPresentation>>(
                new HostResult<LensPresentation>.Success(presentation));
        }

        public Task<HostResult<bool>> SelectAsync(
            ImmutableArray<IPlacedObjectId> objects,
            CancellationToken cancellationToken)
        {
            SelectCount++;
            return ClearAsync(cancellationToken);
        }

        public Task<HostResult<bool>> ClearIsolationAsync(CancellationToken cancellationToken) =>
            ClearAsync(cancellationToken);

        public Task<HostResult<bool>> ClearAsync(CancellationToken cancellationToken)
        {
            NativeCalls++;
            return Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(true));
        }

        public Task<string> IsolateObjectsAsync(
            ImmutableArray<IPlacedObjectId> objects,
            CancellationToken cancellationToken)
        {
            NativeCalls++;
            IsolateCount++;
            return Task.FromResult("Fixture isolated.");
        }

        public Task<string> FocusAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken)
        {
            NativeCalls++;
            FocusCount++;
            return Task.FromResult("Fixture focused.");
        }
    }
}
