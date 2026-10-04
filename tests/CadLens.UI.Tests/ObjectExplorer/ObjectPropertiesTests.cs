using System.Collections.Immutable;
using System.IO;
using CadLens.Lenses;
using CadLens.Common;
using Xunit;

namespace CadLens.UI.Tests;

/// <summary>Property exploration keeps numeric ordering, targets, and persistent choices consistent.</summary>
[Collection("WPF")]
public sealed class ObjectPropertiesTests
{
    /// <summary>Curve and hatch areas share localized display, exact grouping, sorting, and numeric filtering.</summary>
    [Theory]
    [InlineData(DrawingGrouping.Layers, LanguagePreference.English, "AcDbEllipse", "Area", "12.35", "12.3451")]
    [InlineData(DrawingGrouping.Layers, LanguagePreference.Russian, "AcDbHatch", "Площадь", "12,35", "12,3451")]
    [InlineData(DrawingGrouping.ObjectTypes, LanguagePreference.English, "AcDbHatch", "Area", "12.35", "12.3451")]
    [InlineData(DrawingGrouping.ObjectTypes, LanguagePreference.Russian, "AcDbEllipse", "Площадь", "12,35", "12,3451")]
    public async Task AreasUseSharedPropertyExploration(
        DrawingGrouping grouping,
        LanguagePreference language,
        string typeKey,
        string label,
        string rounded,
        string threshold)
    {
        var previous = UiText.Current.Preference;

        try
        {
            UiText.Current.Select(language, persist: false);
            var layer = new LayerId("Roads");
            var inventory = new DrawingInventory(
                "Model",
                [new LayerSnapshot(layer, "Roads", false, false, false, false)],
                [Entity(1, 12.3451), Entity(2, 12.3452), Entity(3, null), Entity(4, 0)],
                new DrawingPrecision(2));
            var actions = new Actions(inventory);
            using var model = new ObjectExplorerViewModel(actions, grouping);
            await model.ActivateAsync(CancellationToken.None);

            if (grouping == DrawingGrouping.Layers)
                await model.EnterCommand.ExecuteAsync(Assert.Single(model.Items));

            await model.EnterCommand.ExecuteAsync(Assert.Single(model.Items));
            ShowProperty(model, DrawingPropertyId.Area);
            Assert.Equal(label, UiText.Current.Get(model.SortByCountLabel));
            Assert.Equal(["4", "1", "2", "3"], model.Items.Select(node => node.Id));
            var displayProperty = model.DisplayPropertyId;
            var precision = model.Precision;
            Assert.Equal(
                ["0", rounded, rounded, "—"],
                model.Items.Select(node => DrawingValueFormatter.FormatMetric(node, displayProperty, precision)));
            var detail = Assert.Single(model.Items[1].Fields, field => field.Label == "Area");
            Assert.Equal(new DrawingNumberValue(12.3451, DrawingUnit.Area), detail.TypedValue);
            Assert.Equal(rounded, DrawingValueFormatter.FormatDetail(detail, model.Precision));

            await Toggle(model, DrawingPropertyId.Area);
            Assert.Equal(4, model.Items.Length);
            Assert.Equal(2, model.Items.Count(node => DrawingValueFormatter.FormatLabel(node, model.Precision) == $"{label}: {rounded}"));
            model.PropertyFilter.PropertyId = DrawingPropertyId.Area;
            Assert.Equal(
                UiText.Current.Get("Enter square drawing units without digit grouping."),
                model.PropertyFilter.InputHint);
            model.PropertyFilter.Operator = DrawingFilterOperator.GreaterThan;
            model.PropertyFilter.InputText = threshold;
            await model.ApplyPropertyFilterCommand.ExecuteAsync(null);

            Assert.Equal(new DrawingNumberValue(12.3451, DrawingUnit.Area), model.AppliedPropertyFilter!.Value);
            Assert.Equal(["2"], model.Current!.Objects.Select(id => id.DisplayId));
            Assert.Single(model.Items);
            Assert.Equal(1, actions.ReadCount);

            EntitySnapshot Entity(int id, double? area) => new(
                new TestEntityId(id),
                layer,
                typeKey,
                ImmutableDictionary<DrawingPropertyId, DrawingValue?>.Empty.Add(
                    DrawingPropertyId.Area,
                    area is { } value ? new DrawingNumberValue(value, DrawingUnit.Area) : null));
        }
        finally
        {
            UiText.Current.Select(previous, persist: false);
        }
    }

    /// <summary>The displayed metric order is also the Previous/Next order, with unavailable values last.</summary>
    [Theory]
    [InlineData(DrawingGrouping.Layers)]
    [InlineData(DrawingGrouping.ObjectTypes)]
    public async Task NumericSortingDrivesObjectNavigation(DrawingGrouping grouping)
    {
        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, grouping);
        await EnterPolylines(model, grouping);
        var calls = actions.EffectCalls;

        model.SortByCountCommand.Execute(null);
        Assert.Equal(["3", "2", "1", "4"], model.Items.Select(node => node.Id));
        Assert.Equal("Vertices", model.SortByCountLabel);
        Assert.Equal(1, actions.ReadCount);
        Assert.Equal(calls, actions.EffectCalls);
        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        Assert.Equal("1 of 4", model.ObjectPosition);
        await model.NextCommand.ExecuteAsync(null);
        Assert.Equal("2", model.Current!.Id);
        await model.PreviousCommand.ExecuteAsync(null);
        Assert.Equal("3", model.Current.Id);

        await model.BackCommand.ExecuteAsync(null);
        model.SortByCountCommand.Execute(null);
        Assert.Equal(["1", "2", "3", "4"], model.Items.Select(node => node.Id));
    }

    /// <summary>Measurements and unavailable facts remain selectable without a per-type field catalog.</summary>
    [Fact]
    public async Task ObservedMeasurementsCanBeCombinedForGrouping()
    {
        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.ObjectTypes);
        await EnterPolylines(model, DrawingGrouping.ObjectTypes);
        Assert.Contains(model.GroupingOptions, option => option.Id == DrawingPropertyId.Vertices);
        await Toggle(model, DrawingPropertyId.Vertices);
        await Toggle(model, DrawingPropertyId.Color);

        Assert.Equal(4, model.Items.Length);
        Assert.Equal("Color + Vertices", model.GroupingSummary);
        Assert.All(model.Items, node => Assert.Equal(2, node.Properties.Length));
        Assert.Contains(
            model.Items,
            node => node.Properties.Any(property =>
                property is {Id: DrawingPropertyId.Vertices, Value: null}));
        Assert.Equal(1, actions.ReadCount);
    }

    /// <summary>Changing the displayed property updates typed sorting and the active object's sibling order.</summary>
    [Theory]
    [InlineData(DrawingGrouping.Layers)]
    [InlineData(DrawingGrouping.ObjectTypes)]
    public async Task SelectedPropertyControlsRowsAndObjectNavigation(DrawingGrouping grouping)
    {
        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, grouping);
        await EnterPolylines(model, grouping);
        Assert.Equal(DrawingPropertyId.Vertices, model.DisplayPropertyId);
        var calls = actions.EffectCalls;
        ShowProperty(model, DrawingPropertyId.Length);

        Assert.True(model.IsCountSortActive);
        Assert.Equal("Length", model.SortByCountLabel);
        Assert.Equal(["3", "2", "1", "4"], model.Items.Select(node => node.Id));
        var displayProperty = model.DisplayPropertyId;
        Assert.Equal(
            ["2", "92", "100", "—"],
            model.Items.Select(node => DrawingValueFormatter.FormatMetric(node, displayProperty)));
        Assert.Equal(calls, actions.EffectCalls);
        await model.EnterCommand.ExecuteAsync(model.Items[0]);
        await model.NextCommand.ExecuteAsync(null);
        Assert.Equal("2", model.Current!.Id);
        calls = actions.EffectCalls;
        ShowProperty(model, DrawingPropertyId.Linetype);

        Assert.Equal("2", model.Current.Id);
        Assert.Equal("4 of 4", model.ObjectPosition);
        Assert.Equal(calls, actions.EffectCalls);
        await model.PreviousCommand.ExecuteAsync(null);
        Assert.Equal("4", model.Current.Id);
        await model.BackCommand.ExecuteAsync(null);
        Assert.Equal(["1", "3", "4", "2"], model.Items.Select(node => node.Id));
        Assert.Equal("Linetype", model.SortByCountLabel);
        Assert.Equal("None", model.GroupingSummary);
        model.SortByCountCommand.Execute(null);
        Assert.Equal(["2", "1", "3", "4"], model.Items.Select(node => node.Id));
        Assert.Equal(1, actions.ReadCount);
    }

    /// <summary>Choices follow included type targets and stay available while browsing a narrower subgroup.</summary>
    [Theory]
    [InlineData(DrawingGrouping.Layers)]
    [InlineData(DrawingGrouping.ObjectTypes)]
    public async Task PropertyChoicesUseTheIncludedTypeScope(DrawingGrouping grouping)
    {
        var roads = new LayerId("Roads");
        var other = new LayerId("Other");
        var hidden = new LayerId("Hidden");
        var inventory = new DrawingInventory(
            "Model",
            [
                new LayerSnapshot(roads, "Roads", false, false, false, false),
                new LayerSnapshot(other, "Other", false, false, false, false),
                new LayerSnapshot(hidden, "Hidden", true, false, false, false)
            ],
            [
                Hatch(1, roads, (DrawingPropertyId.FillKind, new DrawingTextValue("Solid", true))),
                Hatch(
                    2,
                    roads,
                    (DrawingPropertyId.FillKind, new DrawingTextValue("Pattern", true)),
                    (DrawingPropertyId.Pattern, new DrawingTextValue("ANSI31"))),
                Hatch(
                    3,
                    other,
                    (DrawingPropertyId.FillKind, new DrawingTextValue("Gradient", true)),
                    (DrawingPropertyId.Gradient, new DrawingTextValue("LINEAR"))),
                Hatch(4, hidden, (DrawingPropertyId.PatternScale, new DrawingNumberValue(2, DrawingUnit.Scale)))
            ]);
        using var model = new ObjectExplorerViewModel(new Actions(inventory), grouping);
        await model.ActivateAsync(CancellationToken.None);

        if (grouping == DrawingGrouping.Layers)
            await model.EnterCommand.ExecuteAsync(model.Items.Single(node => node.Id == "Roads"));

        await model.EnterCommand.ExecuteAsync(Assert.Single(model.Items));
        Assert.Contains(model.GroupingOptions, option => option.Id == DrawingPropertyId.Pattern);
        Assert.DoesNotContain(model.GroupingOptions, option => option.Id == DrawingPropertyId.PatternScale);
        Assert.Equal(
            grouping == DrawingGrouping.ObjectTypes,
            model.GroupingOptions.Any(option => option.Id == DrawingPropertyId.Gradient));
        await Toggle(model, DrawingPropertyId.FillKind);
        await model.EnterCommand.ExecuteAsync(model.Items.Single(node => node.Objects.Contains(new TestEntityId(1))));
        Assert.Contains(model.DisplayPropertyOptions, option => option.Id == DrawingPropertyId.Pattern);
        ShowProperty(model, DrawingPropertyId.Pattern);
        Assert.Equal("—", DrawingValueFormatter.FormatMetric(Assert.Single(model.Items), model.DisplayPropertyId));
        return;

        static EntitySnapshot Hatch(
            int id,
            LayerId layer,
            params (DrawingPropertyId Id, DrawingValue? Value)[] properties) =>
            new(
                new TestEntityId(id),
                layer,
                "AcDbHatch",
                properties.ToImmutableDictionary(property => property.Id, property => property.Value));
    }

    /// <summary>Combined grouping uses the snapshot and preserves a selected object through ancestor changes.</summary>
    [Theory]
    [InlineData(DrawingGrouping.Layers)]
    [InlineData(DrawingGrouping.ObjectTypes)]
    public async Task CompositeGroupingPreservesTargetsAndSelectedObject(DrawingGrouping grouping)
    {
        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, grouping);
        await EnterPolylines(model, grouping);
        var targets = model.Current!.Objects;
        var calls = actions.EffectCalls;
        await Toggle(model, DrawingPropertyId.Color);
        await Toggle(model, DrawingPropertyId.Linetype);
        await Toggle(model, DrawingPropertyId.Lineweight);

        Assert.Equal(1, actions.ReadCount);
        Assert.Equal(calls, actions.EffectCalls);
        Assert.Equal(3, model.Items.Length);
        Assert.Equal(
            model.Items.Select(DrawingValueFormatter.FormatLabel).OrderBy(item => item, StringComparer.OrdinalIgnoreCase),
            model.Items.Select(DrawingValueFormatter.FormatLabel));
        Assert.Equal(targets.Length, model.Items.Sum(node => node.Count));
        Assert.Equal(
            targets.OrderBy(id => id.DisplayId),
            model.Items.SelectMany(node => node.Objects).OrderBy(id => id.DisplayId));
        var group = model.Items.Single(node => node.Count == 2);
        await model.EnterCommand.ExecuteAsync(group);
        Assert.Equal("Vertices", model.SortByCountLabel);
        await model.EnterCommand.ExecuteAsync(model.Items.Single(node => node.Id == "1"));
        await Toggle(model, DrawingPropertyId.Linetype);

        Assert.Equal("1", model.Current!.Id);
        Assert.Equal(new TestEntityId(1), Assert.Single(model.Current.Objects));
        Assert.Contains(model.Breadcrumbs, node => node.Kind == LensNodeKind.PropertyGroup);
        Assert.Equal(1, actions.ReadCount);
        Assert.Equal("Color + Lineweight", model.GroupingSummary);
    }

    /// <summary>When a subgroup disappears, Auto effects follow the restored broader target.</summary>
    [Fact]
    public async Task RegroupingReconcilesChangedGroupTargets()
    {
        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.ObjectTypes);
        await EnterPolylines(model, DrawingGrouping.ObjectTypes);
        await Toggle(model, DrawingPropertyId.Color);
        await model.EnterCommand.ExecuteAsync(model.Items.Single(node => node.Count == 3));
        await model.ToggleAutoSelectCommand.ExecuteAsync(null);
        await model.ToggleAutoIsolationCommand.ExecuteAsync(null);
        Assert.Equal(3, actions.Selected.Length);
        await Toggle(model, DrawingPropertyId.Linetype);

        Assert.Equal(LensNodeKind.Type, model.Current!.Kind);
        Assert.Equal(4, actions.Selected.Length);
        Assert.Equal(model.Current.Objects, actions.Isolated);
        Assert.Equal(1, actions.ReadCount);
    }

    /// <summary>Grouping choices reopen per lens and primitive type without reading before activation.</summary>
    [Fact]
    public async Task GroupingPreferencesAreIndependentAndRestoreBeforeReading()
    {
        using var file = new SettingsFile();
        using var layers = new ObjectExplorerViewModel(new Actions(), DrawingGrouping.Layers, file.Service);
        using var objects = new ObjectExplorerViewModel(new Actions(), DrawingGrouping.ObjectTypes, file.Service);
        await EnterPolylines(layers, DrawingGrouping.Layers);
        await EnterPolylines(objects, DrawingGrouping.ObjectTypes);
        await Toggle(layers, DrawingPropertyId.Color);
        await Toggle(layers, DrawingPropertyId.Lineweight);
        await Toggle(objects, DrawingPropertyId.Linetype);
        ShowProperty(layers, DrawingPropertyId.Length);
        ShowProperty(objects, DrawingPropertyId.Linetype);
        await objects.RootCommand.ExecuteAsync(null);
        await objects.EnterCommand.ExecuteAsync(objects.Items.Single(node => node.Id == "AcDbHatch"));
        await Toggle(objects, DrawingPropertyId.Pattern);
        ShowProperty(objects, DrawingPropertyId.Pattern);

        var actions = new Actions();
        using var reopened = new ObjectExplorerViewModel(actions, DrawingGrouping.ObjectTypes, file.Service);
        Assert.Equal(0, actions.ReadCount);
        await EnterPolylines(reopened, DrawingGrouping.ObjectTypes);
        Assert.Equal([DrawingPropertyId.Linetype], SelectedFields(reopened));
        Assert.Equal(DrawingPropertyId.Linetype, reopened.DisplayPropertyId);
        await reopened.RootCommand.ExecuteAsync(null);
        await reopened.EnterCommand.ExecuteAsync(reopened.Items.Single(node => node.Id == "AcDbHatch"));
        Assert.Equal([DrawingPropertyId.Pattern], SelectedFields(reopened));
        Assert.Equal(DrawingPropertyId.Pattern, reopened.DisplayPropertyId);

        using var reopenedLayers = new ObjectExplorerViewModel(new Actions(), DrawingGrouping.Layers, file.Service);
        await EnterPolylines(reopenedLayers, DrawingGrouping.Layers);
        Assert.Equal([DrawingPropertyId.Color, DrawingPropertyId.Lineweight], SelectedFields(reopenedLayers));
        Assert.Equal(DrawingPropertyId.Length, reopenedLayers.DisplayPropertyId);
    }

    /// <summary>Unknown saved property identities cannot create invalid grouping controls or host work.</summary>
    [Fact]
    public async Task InvalidSavedGroupingFieldsAreIgnored()
    {
        using var file = new SettingsFile();
        var path = Path.Combine(file.Directory, "lens-object-types.json");
        const string settings = """{"PropertyGrouping":{"AcDbPolyline":["Color","Color","RemovedProperty","9999"]},"DisplayProperties":{"AcDbPolyline":"9999"}}""";
#if NETFRAMEWORK
        File.WriteAllText(path, settings);
#else
        await File.WriteAllTextAsync(path, settings);
#endif
        var actions = new Actions();
        using var model = new ObjectExplorerViewModel(actions, DrawingGrouping.ObjectTypes, file.Service);
        Assert.Equal(0, actions.ReadCount);
        await EnterPolylines(model, DrawingGrouping.ObjectTypes);
        Assert.Equal([DrawingPropertyId.Color], SelectedFields(model));
        Assert.Equal(2, model.Items.Length);
        Assert.Equal(1, actions.ReadCount);
        Assert.Equal(DrawingPropertyId.Vertices, model.DisplayPropertyId);
    }

    private static IEnumerable<DrawingPropertyId> SelectedFields(ObjectExplorerViewModel model) =>
        model.GroupingOptions.Where(option => option.IsSelected).Select(option => option.Id);

    private static Task Toggle(ObjectExplorerViewModel model, DrawingPropertyId id) =>
        model.ToggleGroupingCommand.ExecuteAsync(model.GroupingOptions.Single(option => option.Id == id));

    private static void ShowProperty(ObjectExplorerViewModel model, DrawingPropertyId id) =>
        model.SelectDisplayPropertyCommand.Execute(model.DisplayPropertyOptions.Single(option => option.Id == id));

    private static async Task EnterPolylines(ObjectExplorerViewModel model, DrawingGrouping grouping)
    {
        await model.ActivateAsync(CancellationToken.None);

        if (grouping == DrawingGrouping.Layers)
            await model.EnterCommand.ExecuteAsync(Assert.Single(model.Items));

        await model.EnterCommand.ExecuteAsync(model.Items.Single(node => node.Id == "AcDbPolyline"));
    }

    private static DrawingInventory Inventory()
    {
        var layer = new LayerId("Roads");
        return new DrawingInventory(
            "Model",
            [new LayerSnapshot(layer, "Roads", false, false, false, false)],
            [
                Polyline(1, 2, 1, "Continuous", 25),
                Polyline(2, 10, 1, "Dashed", 50),
                Polyline(3, 100, 5, "Continuous", 25),
                Polyline(4, null, 1, "Continuous", 25),
                new EntitySnapshot(
                    new TestEntityId(5),
                    layer,
                    "AcDbHatch",
                    new Dictionary<DrawingPropertyId, DrawingValue?>
                    {
                        [DrawingPropertyId.BoundaryLoops] = new DrawingNumberValue(2, DrawingUnit.Count),
                        [DrawingPropertyId.Pattern] = new DrawingTextValue("ANSI31"),
                        [DrawingPropertyId.PatternAngle] = new DrawingNumberValue(0, DrawingUnit.Angle),
                        [DrawingPropertyId.PatternScale] = new DrawingNumberValue(1, DrawingUnit.Scale)
                    }.ToImmutableDictionary(),
                    DrawingPropertyId.BoundaryLoops)
            ]);

        EntitySnapshot Polyline(int id, int? vertices, int color, string linetype, int weight) => new(
            new TestEntityId(id),
            layer,
            "AcDbPolyline",
            new Dictionary<DrawingPropertyId, DrawingValue?>
            {
                [DrawingPropertyId.Color] = new DrawingColorValue(new AssignedColor(AssignedColorKind.Index, color)),
                [DrawingPropertyId.Linetype] = new DrawingTextValue(linetype),
                [DrawingPropertyId.Lineweight] =
                    new DrawingLineweightValue(new AssignedLineweight(AssignedLineweightKind.Explicit, weight)),
                [DrawingPropertyId.LinetypeScale] = new DrawingNumberValue(1, DrawingUnit.Scale),
                [DrawingPropertyId.Transparency] =
                    new DrawingTransparencyValue(new AssignedTransparency(AssignedTransparencyKind.Explicit)),
                [DrawingPropertyId.Vertices] =
                    vertices.HasValue ? new DrawingNumberValue(vertices.Value, DrawingUnit.Count) : null,
                [DrawingPropertyId.Length] =
                    vertices.HasValue ? new DrawingNumberValue(102 - vertices.Value, DrawingUnit.Distance) : null,
                [DrawingPropertyId.Closed] = new DrawingBooleanValue(false)
            }.ToImmutableDictionary(),
            DrawingPropertyId.Vertices);
    }

    private sealed record LayerId(string DisplayId) : ILayerId;

    private sealed class Actions(DrawingInventory? inventory = null) : IObjectExplorerActions
    {
        private readonly DrawingLensProvider _provider = new(new Source(inventory ?? Inventory()));
        internal int ReadCount { get; private set; }
        internal int EffectCalls { get; private set; }
        internal ImmutableArray<IPlacedObjectId> Selected { get; private set; } = [];
        internal ImmutableArray<IPlacedObjectId> Isolated { get; private set; } = [];

        public Task<HostResult<LensPresentation>> ReadAsync(
            DrawingGrouping grouping,
            IReadOnlyCollection<string> enabledFilters,
            ImmutableArray<IPlacedObjectId>? selectedObjects,
            CancellationToken cancellationToken)
        {
            ReadCount++;
            return _provider.LoadAsync(grouping, enabledFilters, selectedObjects, cancellationToken);
        }

        public Task<HostResult<ImmutableArray<IPlacedObjectId>>> RequestObjectsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<HostResult<ImmutableArray<IPlacedObjectId>>>(new HostResult<ImmutableArray<IPlacedObjectId>>.Success([]));

        public void ClearImmediately(bool hostTerminating)
        {
            EffectCalls++;
            Selected = [];
            Isolated = [];
        }

        public Task<HostResult<bool>> SelectAsync(
            ImmutableArray<IPlacedObjectId> objects,
            CancellationToken cancellationToken)
        {
            EffectCalls++;
            Selected = objects;
            return Success();
        }

        public Task<HostResult<bool>> ClearIsolationAsync(CancellationToken cancellationToken)
        {
            EffectCalls++;
            Isolated = [];
            return Success();
        }

        public Task<HostResult<bool>> ClearAsync(CancellationToken cancellationToken)
        {
            ClearImmediately(false);
            return Success();
        }

        public Task<string> IsolateObjectsAsync(
            ImmutableArray<IPlacedObjectId> objects,
            CancellationToken cancellationToken)
        {
            EffectCalls++;
            Isolated = objects;
            return Task.FromResult("Isolated.");
        }

        public Task<string> FocusAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken)
        {
            EffectCalls++;
            return Task.FromResult("Focused.");
        }

        private static Task<HostResult<bool>> Success() =>
            Task.FromResult<HostResult<bool>>(new HostResult<bool>.Success(true));
    }

    private sealed class Source(DrawingInventory inventory) : IDrawingInventorySource
    {
        public Task<HostResult<DrawingInventory>> ReadAsync(ImmutableArray<IPlacedObjectId>? selectedObjects, CancellationToken cancellationToken) =>
            Task.FromResult<HostResult<DrawingInventory>>(new HostResult<DrawingInventory>.Success(inventory));
    }
}