using System.Collections.Immutable;
using Common;

namespace CadLens.Lenses;

/// <summary>Builds Layers and Object Types from one host-independent inventory.</summary>
/// <param name="source">Detached snapshot source.</param>
public sealed class DrawingLensProvider(IDrawingInventorySource source) : IDrawingLensProvider
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
        DrawingGrouping grouping,
        IReadOnlySet<string> enabledFilters,
        CancellationToken cancellationToken)
    {
        // Capture options before awaiting: later UI changes belong to the next request.
        var includeFrozen = enabledFilters.Contains(IncludeFrozen);
        var includeOff = enabledFilters.Contains(IncludeOff);
        var result = await source.ReadAsync(cancellationToken).ConfigureAwait(false);

        return result.Bind(snapshot => BuildPresentation(
            snapshot,
            grouping,
            includeFrozen,
            includeOff,
            cancellationToken));
    }

    private static HostResult<LensPresentation> BuildPresentation(
        DrawingInventory snapshot,
        DrawingGrouping grouping,
        bool includeFrozen,
        bool includeOff,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var layers = snapshot.Layers
            .Where(layer => IsIncluded(layer, includeFrozen, includeOff))
            .ToDictionary(layer => layer.Id);
        var entities = snapshot.Entities.Where(entity => layers.ContainsKey(entity.LayerId)).ToList();
        var groups = grouping switch
        {
            DrawingGrouping.Layers => CreateLayerGroups(entities, layers, cancellationToken),
            DrawingGrouping.ObjectTypes => CreateTypeGroups(entities, layers, null, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(grouping), grouping, "Unknown drawing grouping.")
        };
        var isLayers = grouping == DrawingGrouping.Layers;
        var presentation = new LensPresentation(
            isLayers ? "Layers" : "Object Types",
            snapshot.SpaceLabel,
            groups,
            Filters,
            isLayers
                ? "No layers contain included objects in the current space."
                : "No types contain included objects in the current space.",
            isLayers ? "All layers" : "All types",
            isLayers ? "Search layers" : "Search types",
            isLayers ? "layers" : "types");

        return new HostResult<LensPresentation>.Success(presentation);
    }

    private static ImmutableArray<LensNode> CreateLayerGroups(
        IEnumerable<EntitySnapshot> entities,
        Dictionary<ILayerId, LayerSnapshot> layers,
        CancellationToken cancellationToken)
    {
        var entitiesByLayer = entities.GroupBy(entity => entity.LayerId)
            .OrderBy(group => layers[group.Key].Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Key.DisplayId, StringComparer.Ordinal);

        return
            [.. entitiesByLayer.Select(group => CreateLayerNode(layers[group.Key], group, layers, cancellationToken))];
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
        Dictionary<ILayerId, LayerSnapshot> layers,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var types = CreateTypeGroups(entities, layers, layer, cancellationToken);

        return new LensNode(
            layer.Id.DisplayId,
            layer.Name,
            [.. types.SelectMany(type => type.Objects)],
            types,
            CreateLayerDetails(layer),
            [LensAction.Focus]);
    }

    private static ImmutableArray<LensNode> CreateTypeGroups(
        IEnumerable<EntitySnapshot> entities,
        Dictionary<ILayerId, LayerSnapshot> layers,
        LayerSnapshot? layer,
        CancellationToken cancellationToken) =>
    [
        .. entities.GroupBy(entity => entity.TypeKey, StringComparer.Ordinal)
            .OrderBy(group => group.Key.GetTypeLabel(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => CreateTypeNode(group, layers, layer, cancellationToken))
    ];

    private static LensNode CreateTypeNode(
        IGrouping<string, EntitySnapshot> entities,
        Dictionary<ILayerId, LayerSnapshot> layers,
        LayerSnapshot? layer,
        CancellationToken cancellationToken)
    {
        var label = entities.Key.GetTypeLabel();
        var objects = entities.OrderBy(entity => entity.Id.DisplayId, StringComparer.Ordinal)
            .Select(entity => CreateObjectNode(layers[entity.LayerId], entity, label, cancellationToken))
            .ToImmutableArray();

        return new LensNode(
            entities.Key,
            label,
            [.. objects.SelectMany(node => node.Objects)],
            objects,
            layer is null
                ? [new DetailField("Primitive type", label)]
                : [new DetailField("Layer", layer.Name), new DetailField("Primitive type", label)],
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
            entity.Id.DisplayId,
            $"{typeLabel} {entity.Id.DisplayId}",
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

        return hiddenReasons.Count == 0
            ? "Layer is on and thawed"
            : $"Hidden: {string.Join(", ", hiddenReasons)}. Inclusion and Focus do not reveal it.";
    }
}
