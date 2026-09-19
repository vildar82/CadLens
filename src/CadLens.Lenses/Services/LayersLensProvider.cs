using System.Collections.Immutable;
using CadLens.Core;

namespace CadLens.Lenses;

/// <summary>Builds the Layers lens from a host-independent inventory.</summary>
/// <param name="source">Detached snapshot source.</param>
public sealed class LayersLensProvider(ILayersSnapshotSource source) : ILensProvider
{
    /// <summary>Option identity for globally or viewport-frozen layers.</summary>
    public const string IncludeFrozen = "include-frozen";

    /// <summary>Option identity for switched-off layers.</summary>
    public const string IncludeOff = "include-off";

    private static readonly ImmutableArray<BooleanFilter> Filters =
    [
        new(
            IncludeFrozen,
            "Include frozen",
            "Include globally and viewport-frozen layers without revealing them.",
            IconRole.Snowflake),
        new(IncludeOff, "Include off", "Include switched-off layers without switching them on.", IconRole.Lightbulb)
    ];

    /// <inheritdoc />
    public async Task<HostResult<LensPresentation>> LoadAsync(
        IReadOnlySet<string> enabledFilters,
        CancellationToken cancellationToken)
    {
        // Capture options before awaiting: later UI changes belong to the next request.
        var includeFrozen = enabledFilters.Contains(IncludeFrozen);
        var includeOff = enabledFilters.Contains(IncludeOff);
        var result = await source.ReadAsync(cancellationToken).ConfigureAwait(false);

        return result.Bind(snapshot => BuildPresentation(
            snapshot,
            includeFrozen,
            includeOff,
            cancellationToken));
    }

    private static HostResult<LensPresentation> BuildPresentation(
        LayersSnapshot snapshot,
        bool includeFrozen,
        bool includeOff,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var groups = CreateLayerGroups(snapshot, includeFrozen, includeOff, cancellationToken);
        var presentation = new LensPresentation(
            "layers",
            "Layers",
            snapshot.SpaceLabel,
            groups,
            Filters,
            "No layers contain included objects in the current space.");

        return new HostResult<LensPresentation>.Success(presentation);
    }

    private static ImmutableArray<LensNode> CreateLayerGroups(
        LayersSnapshot snapshot,
        bool includeFrozen,
        bool includeOff,
        CancellationToken cancellationToken)
    {
        var entitiesByLayer = snapshot.Entities.ToLookup(entity => entity.LayerId, StringComparer.Ordinal);
        var includedLayers = snapshot.Layers
            .Where(layer => IsIncluded(layer, includeFrozen, includeOff))
            .Where(layer => entitiesByLayer.Contains(layer.Id))
            .OrderBy(layer => layer.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(layer => layer.Id, StringComparer.Ordinal);

        return includedLayers
            .Select(layer => CreateLayerNode(layer, entitiesByLayer[layer.Id], cancellationToken))
            .ToImmutableArray();
    }

    private static bool IsIncluded(LayerSnapshot layer, bool includeFrozen, bool includeOff)
    {
        if (layer.IsOff && !includeOff)
            return false;

        var isFrozen = layer.IsFrozen || layer.IsViewportFrozen;

        return !isFrozen || includeFrozen;
    }

    private static LensNode CreateLayerNode(
        LayerSnapshot layer,
        IEnumerable<EntitySnapshot> entities,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var types = entities.GroupBy(entity => entity.TypeKey, StringComparer.Ordinal)
            .OrderBy(group => group.Key.GetTypeLabel(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => CreateTypeNode(layer, group, cancellationToken))
            .ToImmutableArray();

        return new LensNode(
            layer.Id,
            layer.Name,
            [.. types.SelectMany(type => type.Objects)],
            types,
            CreateLayerDetails(layer),
            [LensAction.Focus]);
    }

    private static LensNode CreateTypeNode(
        LayerSnapshot layer,
        IGrouping<string, EntitySnapshot> entities,
        CancellationToken cancellationToken)
    {
        var label = entities.Key.GetTypeLabel();
        var objects = entities.OrderBy(entity => entity.Id.ToString(), StringComparer.Ordinal)
            .Select(entity => CreateObjectNode(layer, entity, label, cancellationToken))
            .ToImmutableArray();

        return new LensNode(
            entities.Key,
            label,
            [.. objects.SelectMany(node => node.Objects)],
            objects,
            [new DetailField("Layer", layer.Name), new DetailField("Primitive type", label)],
            [LensAction.Focus]);
    }

    private static LensNode CreateObjectNode(
        LayerSnapshot layer,
        EntitySnapshot entity,
        string typeLabel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return new LensNode(
            entity.Id.ToString(),
            $"{typeLabel} {entity.Id}",
            [entity.Id],
            [],
            [.. CreateLayerDetails(layer), new DetailField("Primitive type", typeLabel)],
            [LensAction.Focus]);
    }

    private static ImmutableArray<DetailField> CreateLayerDetails(LayerSnapshot layer) =>
    [
        new("Layer", layer.Name),
        new("Visibility", DescribeVisibility(layer)),
        new("Locked", layer.IsLocked ? "Yes" : "No")
    ];

    private static string DescribeVisibility(LayerSnapshot layer)
    {
        var hiddenReasons = new List<string>();

        if (layer.IsOff)
            hiddenReasons.Add("off");

        if (layer.IsFrozen)
            hiddenReasons.Add("globally frozen");

        if (layer.IsViewportFrozen)
            hiddenReasons.Add("frozen in active viewport");

        if (hiddenReasons.Count == 0)
            return "Layer is on and thawed";

        return $"Hidden: {string.Join(", ", hiddenReasons)}. Inclusion and Focus do not reveal it.";
    }
}