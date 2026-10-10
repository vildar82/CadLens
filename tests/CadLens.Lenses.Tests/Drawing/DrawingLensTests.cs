using System.Collections.Immutable;
using CadLens.Common;
using Xunit;

namespace CadLens.Lenses.Tests;

/// <summary>Behavioral checks over detached drawing fixtures.</summary>
public sealed class DrawingLensTests
{
    /// <summary>Every visibility combination uses independent, conjunctive inclusion controls.</summary>
    [Theory]
    [InlineData(DrawingGrouping.Layers)]
    [InlineData(DrawingGrouping.ObjectTypes)]
    public async Task FiltersCoverAllStatusCombinations(DrawingGrouping grouping)
    {
        foreach (var off in new[] { false, true })
        foreach (var frozen in new[] { false, true })
        foreach (var viewportFrozen in new[] { false, true })
        foreach (var locked in new[] { false, true })
        foreach (var includeOff in new[] { false, true })
        foreach (var includeFrozen in new[] { false, true })
        {
            var layer = new LayerSnapshot(new TestLayerId("roads"), "Roads", off, frozen, viewportFrozen, locked);
            var filters = new HashSet<string>();

            if (includeOff)
                filters.Add(DrawingLensProvider.IncludeOff);

            if (includeFrozen)
                filters.Add(DrawingLensProvider.IncludeFrozen);

            var result = await Load([layer], [Entity("1", "roads", "AcDbLine")], filters, grouping);
            var expected = (!off || includeOff) && (!(frozen || viewportFrozen) || includeFrozen);
            Assert.Equal(expected ? 1 : 0, result.Groups.Length);
        }
    }

    /// <summary>Primitive groups partition all 248 direct entities exactly once.</summary>
    [Fact]
    public async Task TypeTotalsAndOrderingAreStable()
    {
        var entities = Enumerable.Range(0, 248).Select(index => Entity(
            index.ToString("D4"),
            "roads",
            index < 180 ? "AcDbPolyline" : index < 228 ? "AcDbLine" : "AcDbArc")).ToImmutableArray();
        var layers = ImmutableArray.Create(
            new LayerSnapshot(new TestLayerId("roads"), "Roads", false, false, false, false));
        var first = await Load(layers, entities, new HashSet<string>());
        var shuffled = await Load(layers, [.. entities.Reverse()], new HashSet<string>());
        var group = Assert.Single(first.Groups);
        Assert.Equal(248, group.Count);
        Assert.Equal(248, group.Children.Sum(type => type.Count));
        Assert.Equal(180, group.Children.Single(type => type.Label == "Polyline").Count);
        Assert.Equal(48, group.Children.Single(type => type.Label == "Line").Count);
        Assert.Equal(20, group.Children.Single(type => type.Label == "Arc").Count);
        Assert.Equal<IPlacedObjectId>(group.Objects, shuffled.Groups[0].Objects);
        Assert.Equal(248, group.Objects.Distinct().Count());
    }

    /// <summary>Blocks count once, empty layers disappear, and custom types remain distinct.</summary>
    [Fact]
    public async Task DirectBlocksAndCustomTypesRetainTheirIdentities()
    {
        var result = await Load(
            [
                new LayerSnapshot(new TestLayerId("empty"), "Empty", false, false, false, false),
                new LayerSnapshot(new TestLayerId("a"), "alpha", false, false, false, true),
                new LayerSnapshot(new TestLayerId("b"), "Alpha", false, false, false, false)
            ],
            [
                Entity("2", "a", "AcDbBlockReference"), Entity("1", "a", "AcDbBlockReference"),
                Entity("3", "b", "Custom.One"), Entity("4", "b", "Custom.Two")
            ],
            new HashSet<string>());
        Assert.Equal(["a", "b"], result.Groups.Select(group => group.Id));
        var blocks = Assert.Single(result.Groups[0].Children);
        Assert.Equal(2, blocks.Count);
        Assert.Equal(["1", "2"], blocks.Objects.Select(reference => reference.DisplayId));
        Assert.Equal(["Custom.One", "Custom.Two"], result.Groups[1].Children.Select(type => type.Label));
    }

    /// <summary>Included hidden objects explain their status without promising visible isolation.</summary>
    [Fact]
    public async Task HiddenObjectPreviewExplainsItsLayerAndType()
    {
        var result = await Load(
            [new LayerSnapshot(new TestLayerId("a"), "Roads", true, false, true, false)],
            [Entity("1", "a", "AcDbPolyline")],
            new HashSet<string> { DrawingLensProvider.IncludeFrozen, DrawingLensProvider.IncludeOff });
        var item = result.Groups[0].Children[0].Children[0];
        Assert.Contains(new DetailField("Layer", "Roads"), item.Fields);
        Assert.Contains(new DetailField("Primitive type", "Polyline", DetailValueKind.PrimitiveType, new DrawingTextValue("Polyline", true)), item.Fields);
        Assert.Contains(
            "off, frozen in active viewport",
            item.Fields.Single(field => field.Label == "Visibility").Value);
    }

    /// <summary>Type roots merge entities across layers while retaining each object's own metadata.</summary>
    [Fact]
    public async Task ObjectTypesCombineLayerTargetsAndKeepObjectDetails()
    {
        var result = await Load(
            [
                new LayerSnapshot(new TestLayerId("a"), "Roads", false, false, false, true),
                new LayerSnapshot(new TestLayerId("b"), "Utilities", true, false, true, false)
            ],
            [Entity("2", "b", "AcDbLine"), Entity("1", "a", "AcDbLine"), Entity("3", "a", "AcDbPolyline")],
            new HashSet<string> { DrawingLensProvider.IncludeFrozen, DrawingLensProvider.IncludeOff },
            DrawingGrouping.ObjectTypes);
        Assert.Equal("Object Types", result.Label);
        Assert.Equal("All types", result.RootLabel);
        Assert.Equal("Search types", result.SearchPlaceholder);
        Assert.Equal("types", result.GroupLabel);
        Assert.Equal(["AcDbLine", "AcDbPolyline"], result.Groups.Select(group => group.Id));
        var lines = result.Groups[0];
        Assert.Equal(2, lines.Count);
        Assert.Equal(["1", "2"], lines.Objects.Select(id => id.DisplayId));
        Assert.Equal(lines.Objects, lines.Children.SelectMany(node => node.Objects));
        Assert.Contains(new DetailField("Layer", "Roads"), lines.Children[0].Fields);
        Assert.Contains(new DetailField("Locked", "Yes", DetailValueKind.ApplicationText), lines.Children[0].Fields);
        Assert.Contains(new DetailField("Layer", "Utilities"), lines.Children[1].Fields);
        Assert.Contains(new DetailField("Locked", "No", DetailValueKind.ApplicationText), lines.Children[1].Fields);
        Assert.Contains(new DetailField("Primitive type", "Line", DetailValueKind.PrimitiveType, new DrawingTextValue("Line", true)), lines.Children[1].Fields);
        Assert.Contains(
            "off, frozen in active viewport",
            lines.Children[1].Fields.Single(field => field.Label == "Visibility").Value);
        foreach (var node in lines.Children)
        {
            Assert.Empty(node.Children);
            Assert.Contains(LensAction.Focus, node.Actions);
            Assert.Single(node.Objects);
        }
    }

    /// <summary>Raw type keys stay distinct even when their display labels match.</summary>
    [Fact]
    public async Task ObjectTypesPreserveTypeKeysAndStableObjectOrdering()
    {
        ImmutableArray<LayerSnapshot> layers =
        [
            new(new TestLayerId("a"), "Alpha", false, false, false, false),
            new(new TestLayerId("b"), "Beta", false, false, false, false)
        ];
        ImmutableArray<EntitySnapshot> entities =
        [
            Entity("2", "b", "AcDbBlockReference"), Entity("1", "a", "AcDbBlockReference"),
            Entity("4", "a", "Line"), Entity("3", "b", "AcDbLine"), Entity("5", "b", "Custom.Type")
        ];
        var first = await Load(layers, entities, new HashSet<string>(), DrawingGrouping.ObjectTypes);
        var shuffled = await Load(
            [.. layers.Reverse()],
            [.. entities.Reverse()],
            new HashSet<string>(),
            DrawingGrouping.ObjectTypes);
        Assert.Equal(["AcDbBlockReference", "Custom.Type", "AcDbLine", "Line"], first.Groups.Select(node => node.Id));
        Assert.Equal(5, first.Groups.Sum(node => node.Count));
        Assert.Equal(2, first.Groups[0].Count);
        Assert.Equal(["1", "2"], first.Groups[0].Children.Select(node => node.Id));
        Assert.Equal(5, first.Groups.SelectMany(node => node.Objects).Distinct().Count());
        Assert.Equal(first.Groups.Select(node => node.Id), shuffled.Groups.Select(node => node.Id));

        foreach (var (original, reordered) in first.Groups.Zip(
                     shuffled.Groups,
                     (original, reordered) => (original, reordered)))
        {
            Assert.Equal<IPlacedObjectId>(original.Objects, reordered.Objects);
            Assert.Equal(original.Children.Select(node => node.Id), reordered.Children.Select(node => node.Id));
        }
    }

    /// <summary>Entities without layer metadata cannot bypass visibility filters.</summary>
    [Theory]
    [InlineData(DrawingGrouping.Layers)]
    [InlineData(DrawingGrouping.ObjectTypes)]
    public async Task ExcludesEntitiesWithUnknownLayers(DrawingGrouping grouping)
    {
        var result = await Load(
            [new LayerSnapshot(new TestLayerId("a"), "Roads", false, false, false, false)],
            [Entity("1", "a", "AcDbLine"), Entity("2", "missing", "AcDbLine")],
            new HashSet<string> { DrawingLensProvider.IncludeFrozen, DrawingLensProvider.IncludeOff },
            grouping);
        var group = Assert.Single(result.Groups);
        Assert.Equal("1", Assert.Single(group.Objects).DisplayId);
    }

    /// <summary>A refreshed type path survives an object's move to a different included layer.</summary>
    [Fact]
    public async Task ObjectTypeNavigationRetainsObjectAfterLayerMove()
    {
        ImmutableArray<LayerSnapshot> layers =
        [
            new(new TestLayerId("a"), "Roads", false, false, false, false),
            new(new TestLayerId("b"), "Utilities", false, false, false, true)
        ];
        var first = await Load(
            layers,
            [Entity("1", "a", "AcDbLine"), Entity("2", "b", "AcDbLine")],
            new HashSet<string>(),
            DrawingGrouping.ObjectTypes);
        var state = new NavigationState();
        state.Reset(first.Groups, false);
        Assert.True(state.Enter("AcDbLine"));
        Assert.True(state.Enter("1"));
        var refreshed = await Load(
            layers,
            [Entity("1", "b", "AcDbLine"), Entity("2", "b", "AcDbLine")],
            new HashSet<string>(),
            DrawingGrouping.ObjectTypes);
        state.Reset(refreshed.Groups, true);
        Assert.Equal(["AcDbLine", "1"], state.Path.Select(node => node.Id));
        Assert.Equal(1, state.Position);
        Assert.Equal(2, state.ObjectCount);
        Assert.Contains(new DetailField("Layer", "Utilities"), state.Current!.Fields);
        Assert.Contains(new DetailField("Locked", "Yes", DetailValueKind.ApplicationText), state.Current.Fields);
        state.MoveObject(1);
        Assert.Equal("2", state.Current.Id);
        state.GoBackTo(1);
        Assert.Equal(2, state.Current.Count);
        state.GoBackTo(0);
        Assert.Null(state.Current);
    }

    /// <summary>Overlapping requests retain their own grouping and capture options before the host read.</summary>
    [Fact]
    public async Task SharedProviderKeepsConcurrentRequestOptionsIndependent()
    {
        var source = new PendingSource();
        var provider = new DrawingLensProvider(source);
        var layersFilters = new HashSet<string>();
        var typesFilters = new HashSet<string> { DrawingLensProvider.IncludeOff };
        var layersTask = provider.LoadAsync(DrawingGrouping.Layers, layersFilters, null, CancellationToken.None);
        var typesTask = provider.LoadAsync(DrawingGrouping.ObjectTypes, typesFilters, null, CancellationToken.None);
        layersFilters.Add(DrawingLensProvider.IncludeOff);
        typesFilters.Clear();
        source.Completion.SetResult(
            new HostResult<DrawingInventory>.Success(
                new DrawingInventory(
                    "Model",
                    [
                        new LayerSnapshot(new TestLayerId("a"), "Roads", false, false, false, false),
                        new LayerSnapshot(new TestLayerId("b"), "Hidden", true, false, false, false)
                    ],
                    [Entity("1", "a", "AcDbLine"), Entity("2", "b", "AcDbLine")])));
        var layers = Assert.IsType<HostResult<LensPresentation>.Success>(await layersTask).Value;
        var types = Assert.IsType<HostResult<LensPresentation>.Success>(await typesTask).Value;
        Assert.Equal("Layers", layers.Label);
        Assert.Equal("All layers", layers.RootLabel);
        Assert.Equal("Search layers", layers.SearchPlaceholder);
        Assert.Equal("layers", layers.GroupLabel);
        Assert.Equal("a", Assert.Single(layers.Groups).Id);
        Assert.Equal(1, layers.Groups[0].Count);
        Assert.Equal("Object Types", types.Label);
        Assert.Equal("AcDbLine", Assert.Single(types.Groups).Id);
        Assert.Equal(2, types.Groups[0].Count);
    }

    /// <summary>An unavailable drawing is reported without trying to build a presentation.</summary>
    [Fact]
    public async Task ReportsUnavailableDrawing()
    {
        var provider = new DrawingLensProvider(new UnavailableSource());
        var result = await provider.LoadAsync(DrawingGrouping.Layers, new HashSet<string>(), null, CancellationToken.None);

        Assert.Equal("No drawing.", Assert.IsType<HostResult<LensPresentation>.Unavailable>(result).Reason);
    }

    /// <summary>Row colors reuse each layer snapshot while explicit colors and assignment identities remain intact.</summary>
    [Theory]
    [InlineData(DrawingGrouping.Layers)]
    [InlineData(DrawingGrouping.ObjectTypes)]
    public async Task RowColorsResolveByLayerInBothLenses(DrawingGrouping grouping)
    {
        var layers = ImmutableArray.Create(
            new LayerSnapshot(new TestLayerId("a"), "A", false, false, false, false, 0x123456),
            new LayerSnapshot(new TestLayerId("b"), "B", false, false, false, false, 0xABCDEF));
        var entities = ImmutableArray.Create(
            ColoredEntity("1", "a", AssignedColorKind.ByLayer, null),
            ColoredEntity("2", "b", AssignedColorKind.ByLayer, null),
            ColoredEntity("3", "a", AssignedColorKind.TrueColor, 0x654321),
            ColoredEntity("4", "a", AssignedColorKind.Index, 0xFF0000),
            ColoredEntity("5", "a", AssignedColorKind.ColorBook, 0x00FF00),
            ColoredEntity("6", "a", AssignedColorKind.ByBlock, null),
            Entity("7", "a", "AcDbLine"));
        var result = await Load(layers, entities, new HashSet<string>(), grouping);
        var types = grouping == DrawingGrouping.Layers
            ? result.Groups.SelectMany(node => node.Children).ToList()
            : result.Groups.ToList();
        var objects = types.SelectMany(node => node.Children).OrderBy(node => node.Id).ToList();

        Assert.Equal(
            [0x123456, 0xABCDEF, 0x654321, 0xFF0000, 0x00FF00, null, null],
            objects.Select(node => node.DisplayColor));
        Assert.All(types, node => Assert.Null(node.DisplayColor));
        Assert.Equal(new DrawingColorValue(new AssignedColor(AssignedColorKind.ByLayer)),
            DrawingProperties.GetValue(objects[0], DrawingPropertyId.Color));

        if (grouping == DrawingGrouping.Layers)
            Assert.Equal(layers.Select(layer => layer.DisplayColor), result.Groups.Select(node => node.DisplayColor));
    }

    private static EntitySnapshot ColoredEntity(string key, string layer, AssignedColorKind kind, int? rgb) =>
        Entity(key, layer, "AcDbLine") with
        {
            Properties = ImmutableDictionary<DrawingPropertyId, DrawingValue?>.Empty.Add(
                DrawingPropertyId.Color, new DrawingColorValue(new AssignedColor(kind))),
            DisplayColor = rgb
        };

    private static EntitySnapshot Entity(string key, string layer, string type) =>
        new(new TestEntityId(key), new TestLayerId(layer), type);

    private static async Task<LensPresentation> Load(
        ImmutableArray<LayerSnapshot> layers,
        ImmutableArray<EntitySnapshot> entities,
        IReadOnlyCollection<string> filters,
        DrawingGrouping grouping = DrawingGrouping.Layers)
    {
        var provider = new DrawingLensProvider(new Source(new DrawingInventory("Model", layers, entities)));
        var result = await provider.LoadAsync(grouping, filters, null, CancellationToken.None);
        return Assert.IsType<HostResult<LensPresentation>.Success>(result).Value;
    }

    private sealed class Source(DrawingInventory snapshot) : IDrawingInventorySource
    {
        public Task<HostResult<DrawingInventory>> ReadAsync(ImmutableArray<IPlacedObjectId>? selectedObjects, CancellationToken cancellationToken, int? maximumObjects = null) =>
            Task.FromResult<HostResult<DrawingInventory>>(new HostResult<DrawingInventory>.Success(snapshot));
    }

    private sealed class PendingSource : IDrawingInventorySource
    {
        public TaskCompletionSource<HostResult<DrawingInventory>> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<HostResult<DrawingInventory>> ReadAsync(ImmutableArray<IPlacedObjectId>? selectedObjects, CancellationToken cancellationToken, int? maximumObjects = null) => Completion.Task;
    }

    private sealed class UnavailableSource : IDrawingInventorySource
    {
        public Task<HostResult<DrawingInventory>> ReadAsync(ImmutableArray<IPlacedObjectId>? selectedObjects, CancellationToken cancellationToken, int? maximumObjects = null) =>
            Task.FromResult<HostResult<DrawingInventory>>(new HostResult<DrawingInventory>.Unavailable("No drawing."));
    }
}
