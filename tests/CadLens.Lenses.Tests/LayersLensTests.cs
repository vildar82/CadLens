using System.Collections.Immutable;
using CadLens.Core;
using Common;
using Xunit;

namespace CadLens.Lenses.Tests;

/// <summary>Behavioral checks over detached drawing fixtures.</summary>
public sealed class LayersLensTests
{
    /// <summary>Every visibility combination uses independent, conjunctive inclusion controls.</summary>
    [Fact]
    public async Task FiltersCoverAllStatusCombinations()
    {
        foreach (var off in new[] { false, true })
        foreach (var frozen in new[] { false, true })
        foreach (var viewportFrozen in new[] { false, true })
        foreach (var locked in new[] { false, true })
        foreach (var includeOff in new[] { false, true })
        foreach (var includeFrozen in new[] { false, true })
        {
            var layer = new LayerSnapshot("roads", "Roads", off, frozen, viewportFrozen, locked);
            var filters = new HashSet<string>();

            if (includeOff)
                filters.Add(LayersLensProvider.IncludeOff);

            if (includeFrozen)
                filters.Add(LayersLensProvider.IncludeFrozen);

            var result = await Load([layer], [Entity("1", "roads", "AcDbLine")], filters);
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
        var layers = ImmutableArray.Create(new LayerSnapshot("roads", "Roads", false, false, false, false));
        var first = await Load(layers, entities, new HashSet<string>());
        var shuffled = await Load(layers, entities.Reverse().ToImmutableArray(), new HashSet<string>());
        var group = Assert.Single(first.Groups);
        Assert.Equal(248, group.Count);
        Assert.Equal(248, group.Children.Sum(type => type.Count));
        Assert.Equal(180, group.Children.Single(type => type.Label == "Polyline").Count);
        Assert.Equal(48, group.Children.Single(type => type.Label == "Line").Count);
        Assert.Equal(20, group.Children.Single(type => type.Label == "Arc").Count);
        Assert.Equal(group.Objects.ToArray(), shuffled.Groups[0].Objects.ToArray());
        Assert.Equal(248, group.Objects.Distinct().Count());
    }

    /// <summary>Blocks count once, empty layers disappear, and custom types remain distinct.</summary>
    [Fact]
    public async Task DirectBlocksAndCustomTypesRetainTheirIdentities()
    {
        var result = await Load(
            [
                new("empty", "Empty", false, false, false, false),
                new("a", "alpha", false, false, false, true),
                new("b", "Alpha", false, false, false, false)
            ],
            [
                Entity("2", "a", "AcDbBlockReference"), Entity("1", "a", "AcDbBlockReference"),
                Entity("3", "b", "Custom.One"), Entity("4", "b", "Custom.Two")
            ],
            new HashSet<string>());
        Assert.Equal(new[] { "a", "b" }, result.Groups.Select(group => group.Id));
        var blocks = Assert.Single(result.Groups[0].Children);
        Assert.Equal(2, blocks.Count);
        Assert.Equal(new[] { "1", "2" }, blocks.Objects.Select(reference => reference.ToString()));
        Assert.Equal(new[] { "Custom.One", "Custom.Two" }, result.Groups[1].Children.Select(type => type.Label));
    }

    /// <summary>Included hidden objects explain their status without promising a visible highlight.</summary>
    [Fact]
    public async Task HiddenObjectPreviewExplainsItsLayerAndType()
    {
        var result = await Load(
            [new("a", "Roads", true, false, true, false)],
            [Entity("1", "a", "AcDbPolyline")],
            new HashSet<string> { LayersLensProvider.IncludeFrozen, LayersLensProvider.IncludeOff });
        var item = result.Groups[0].Children[0].Children[0];
        Assert.Contains(new DetailField("Layer", "Roads"), item.Fields);
        Assert.Contains(new DetailField("Primitive type", "Polyline"), item.Fields);
        Assert.Contains(
            "off, frozen in active viewport",
            item.Fields.Single(field => field.Label == "Visibility").Value);
    }

    /// <summary>An unavailable drawing is reported without trying to build a presentation.</summary>
    [Fact]
    public async Task ReportsUnavailableDrawing()
    {
        var provider = new LayersLensProvider(new UnavailableSource());
        var result = await provider.LoadAsync(new HashSet<string>(), default);

        Assert.Equal("No drawing.", Assert.IsType<HostResult<LayersPresentation>.Unavailable>(result).Reason);
    }

    private static EntitySnapshot Entity(string key, string layer, string type) =>
        new(new HostObjectId(key), layer, type);

    private static async Task<LayersPresentation> Load(
        ImmutableArray<LayerSnapshot> layers,
        ImmutableArray<EntitySnapshot> entities,
        IReadOnlySet<string> filters)
    {
        var provider = new LayersLensProvider(new Source(new LayersSnapshot("Model", layers, entities)));
        var result = await provider.LoadAsync(filters, default);
        return Assert.IsType<HostResult<LayersPresentation>.Success>(result).Value;
    }

    private sealed class Source(LayersSnapshot snapshot) : ILayersSnapshotSource
    {
        public Task<HostResult<LayersSnapshot>> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<HostResult<LayersSnapshot>>(new HostResult<LayersSnapshot>.Success(snapshot));
    }

    private sealed class UnavailableSource : ILayersSnapshotSource
    {
        public Task<HostResult<LayersSnapshot>> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<HostResult<LayersSnapshot>>(new HostResult<LayersSnapshot>.Unavailable("No drawing."));
    }
}