using System.Collections.Immutable;
using CadLens.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

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
        IReadOnlyCollection<string> enabledFilters,
        ImmutableArray<IPlacedObjectId>? selectedObjects,
        CancellationToken cancellationToken,
        int? maximumObjects = null)
    {
        // Capture options before awaiting: later UI changes belong to the next request.
        var includeFrozen = enabledFilters.Contains(IncludeFrozen);
        var includeOff = enabledFilters.Contains(IncludeOff);
        var result = await source.ReadAsync(selectedObjects, cancellationToken, maximumObjects).ConfigureAwait(false);

        return result.Bind(snapshot => new HostResult<LensPresentation>.Success(
            Build(snapshot, grouping, includeFrozen, includeOff, null, null, cancellationToken)));
    }

    /// <summary>Rebuilds a drawing lens from detached facts without performing another host read.</summary>
    /// <param name="snapshot">Retained active-space facts.</param>
    /// <param name="grouping">Root grouping of the lens.</param>
    /// <param name="enabledFilters">Current visibility inclusion choices.</param>
    /// <param name="propertyGrouping">Selected combined properties for each runtime type.</param>
    /// <param name="propertyFilters">Optional property condition for each runtime type.</param>
    public static LensPresentation Build(
        DrawingInventory snapshot,
        DrawingGrouping grouping,
        IReadOnlyCollection<string> enabledFilters,
        IReadOnlyDictionary<string, ImmutableArray<DrawingPropertyKey>>? propertyGrouping = null,
        IReadOnlyDictionary<string, DrawingPropertyFilter>? propertyFilters = null) =>
        Build(
            snapshot,
            grouping,
            enabledFilters.Contains(IncludeFrozen),
            enabledFilters.Contains(IncludeOff),
            propertyGrouping,
            propertyFilters,
            CancellationToken.None);

    private static LensPresentation Build(
        DrawingInventory snapshot,
        DrawingGrouping grouping,
        bool includeFrozen,
        bool includeOff,
        IReadOnlyDictionary<string, ImmutableArray<DrawingPropertyKey>>? propertyGrouping,
        IReadOnlyDictionary<string, DrawingPropertyFilter>? propertyFilters,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var layers = snapshot.Layers
            .Where(layer => IsIncluded(layer, includeFrozen, includeOff))
            .ToDictionary(layer => layer.Id);
        var entities = snapshot.Entities.Where(entity => layers.ContainsKey(entity.LayerId)).ToList();
        var precision = snapshot.Precision ?? DrawingPrecision.Default;
        var groups = grouping switch
        {
            DrawingGrouping.Layers => CreateLayerGroups(
                entities,
                layers,
                propertyGrouping,
                propertyFilters,
                precision,
                cancellationToken),
            DrawingGrouping.ObjectTypes => CreateTypeGroups(
                entities,
                layers,
                null,
                propertyGrouping,
                propertyFilters,
                precision,
                cancellationToken),
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
            isLayers ? "layers" : "types",
            snapshot,
            snapshot.Precision);

        return presentation;
    }

    private static ImmutableArray<LensNode> CreateLayerGroups(
        IEnumerable<EntitySnapshot> entities,
        Dictionary<ILayerId, LayerSnapshot> layers,
        IReadOnlyDictionary<string, ImmutableArray<DrawingPropertyKey>>? propertyGrouping,
        IReadOnlyDictionary<string, DrawingPropertyFilter>? propertyFilters,
        DrawingPrecision precision,
        CancellationToken cancellationToken)
    {
        var entitiesByLayer = entities.GroupBy(entity => entity.LayerId)
            .OrderBy(group => layers[group.Key].Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Key.DisplayId, StringComparer.Ordinal);

        return
        [
            .. entitiesByLayer.Select(group => CreateLayerNode(
                layers[group.Key],
                group,
                layers,
                propertyGrouping,
                propertyFilters,
                precision,
                cancellationToken))
        ];
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
        IReadOnlyDictionary<string, ImmutableArray<DrawingPropertyKey>>? propertyGrouping,
        IReadOnlyDictionary<string, DrawingPropertyFilter>? propertyFilters,
        DrawingPrecision precision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var types = CreateTypeGroups(entities, layers, layer, propertyGrouping, propertyFilters, precision, cancellationToken);

        return new LensNode(
            layer.Id.DisplayId,
            layer.Name,
            [.. types.SelectMany(type => type.Objects)],
            types,
            CreateLayerDetails(layer),
            [LensAction.Focus],
            LensNodeKind.Layer,
            DisplayColor: layer.DisplayColor);
    }

    private static ImmutableArray<LensNode> CreateTypeGroups(
        IEnumerable<EntitySnapshot> entities,
        Dictionary<ILayerId, LayerSnapshot> layers,
        LayerSnapshot? layer,
        IReadOnlyDictionary<string, ImmutableArray<DrawingPropertyKey>>? propertyGrouping,
        IReadOnlyDictionary<string, DrawingPropertyFilter>? propertyFilters,
        DrawingPrecision precision,
        CancellationToken cancellationToken) =>
    [
        .. entities.GroupBy(entity => entity.TypeKey, StringComparer.Ordinal)
            .OrderBy(group => group.Key.GetTypeLabel(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => CreateTypeNode(group, layers, layer, propertyGrouping, propertyFilters, precision, cancellationToken))
    ];

    private static LensNode CreateTypeNode(
        IGrouping<string, EntitySnapshot> entities,
        Dictionary<ILayerId, LayerSnapshot> layers,
        LayerSnapshot? layer,
        IReadOnlyDictionary<string, ImmutableArray<DrawingPropertyKey>>? propertyGrouping,
        IReadOnlyDictionary<string, DrawingPropertyFilter>? propertyFilters,
        DrawingPrecision precision,
        CancellationToken cancellationToken)
    {
        var label = entities.Key.GetTypeLabel();
        var filter = propertyFilters?.TryGetValue(entities.Key, out var selectedFilter) == true ? selectedFilter : null;
        var orderedEntities = entities
            .Where(entity => filter is null || filter.Matches(entity, layers[entity.LayerId]))
            .OrderBy(entity => entity.Id.DisplayId, StringComparer.Ordinal)
            .ToList();
        var objects = orderedEntities
            .Select(entity => CreateObjectNode(layers[entity.LayerId], entity, label, cancellationToken))
            .ToImmutableArray();
        var fields = GetGroupingFields(orderedEntities, propertyGrouping);
        var children = fields.IsEmpty ? objects : CreatePropertyGroups(orderedEntities, objects, layers, fields, precision);
        var metric = GetCommonMetric(objects);

        return new LensNode(
            entities.Key,
            label,
            [.. objects.SelectMany(node => node.Objects)],
            children,
            layer is null
                ? [CreatePrimitiveTypeField(entities.Key)]
                : [new DetailField("Layer", layer.Name), CreatePrimitiveTypeField(entities.Key)],
            [LensAction.Focus],
            LensNodeKind.Type,
            entities.Key,
            RowMetric: metric);
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
            DrawingProperties.GetValue(entity, layer, DrawingPropertyId.BlockName) is DrawingTextValue
            {
                Text.Length: > 0
            } blockName
                ? $"{blockName.Text} {entity.Id.DisplayId}"
                : $"{typeLabel} {entity.Id.DisplayId}",
            [entity.Id],
            [],
            [
                .. CreateLayerDetails(layer),
                CreatePrimitiveTypeField(entity.TypeKey),
                .. DrawingProperties.GetDetails(entity, layer),
                .. CreateAttributeDetails(entity)
            ],
            [LensAction.Focus],
            LensNodeKind.Object,
            entity.TypeKey,
            [
                .. DrawingProperties.GetAvailableFields([entity]).Select(id =>
                    new DrawingProperty(id, DrawingProperties.GetValue(entity, layer, id)))
            ],
            RowMetric: DrawingProperties.GetPrimaryMetric(entity),
            DisplayColor: entity.Properties?.GetValueOrDefault(DrawingPropertyId.Color) is DrawingColorValue
                {Color.Kind: AssignedColorKind.ByLayer}
                ? layer.DisplayColor
                : entity.DisplayColor);
    }

    private static IEnumerable<DetailField> CreateAttributeDetails(EntitySnapshot entity)
    {
        var attributes = entity.Properties?
            .Where(pair => pair.Key.Source == DrawingPropertySource.Attribute)
            .Select(pair => (pair.Key, pair.Value))
            .ToList() ?? [];
        var hasCount = entity.Properties?.ContainsKey(DrawingPropertyId.Attributes) == true;
        var count = entity.Properties?.GetValueOrDefault(DrawingPropertyId.Attributes);

        if (!hasCount && attributes.Count == 0)
            yield break;

        yield return new DetailField(
            "Attribute values",
            attributes.Count != 0 ? "" : count is DrawingNumberValue {Value: 0} ? "No attached attributes" : "Unavailable",
            DetailValueKind.ApplicationText);

        foreach (var (key, value) in attributes)
        {
            var text = (value as DrawingTextValue)?.Text;
            yield return new DetailField(
                key.Name,
                text is null ? "Unavailable" : text.Length == 0 ? "(blank)" : text,
                string.IsNullOrEmpty(text) ? DetailValueKind.ApplicationText : DetailValueKind.RawText,
                IsLabelRaw: true,
                PropertyKey: key);
        }
    }

    private static ImmutableArray<DetailField> CreateLayerDetails(LayerSnapshot layer) =>
    [
        new("Layer", layer.Name),
        new("Visibility", DescribeVisibility(layer), DetailValueKind.LayerVisibility),
        new("Locked", layer.IsLocked ? "Yes" : "No", DetailValueKind.ApplicationText)
    ];

    private static DetailField CreatePrimitiveTypeField(string typeKey)
    {
        var label = typeKey.GetTypeLabel();

        return new DetailField(
            "Primitive type",
            label,
            DetailValueKind.PrimitiveType,
            new DrawingTextValue(label, label != typeKey));
    }

    private static ImmutableArray<DrawingPropertyKey> GetGroupingFields(
        List<EntitySnapshot> entities,
        IReadOnlyDictionary<string, ImmutableArray<DrawingPropertyKey>>? propertyGrouping)
    {
        if (entities.Count == 0 || propertyGrouping is null ||
            !propertyGrouping.TryGetValue(entities[0].TypeKey, out var selected) ||
            selected.IsDefaultOrEmpty)
            return [];

        var available = DrawingProperties.GetAvailableFields(entities);

        return [.. selected.Where(available.Contains).Distinct().OrderBy(id => id)];
    }

    private static ImmutableArray<LensNode> CreatePropertyGroups(
        List<EntitySnapshot> entities,
        ImmutableArray<LensNode> objects,
        Dictionary<ILayerId, LayerSnapshot> layers,
        ImmutableArray<DrawingPropertyKey> fields,
        DrawingPrecision precision)
    {
        var groups = new Dictionary<PropertyGroupKey, List<LensNode>>();

        for (var index = 0; index < entities.Count; index++)
        {
            var entity = entities[index];
            var key = new PropertyGroupKey(
            [
                .. fields.Select(id => new DrawingProperty(
                    id,
                    RoundGroupingValue(DrawingProperties.GetValue(entity, layers[entity.LayerId], id), precision)))
            ]);

            if (!groups.TryGetValue(key, out var members))
            {
                members = [];
                groups.Add(key, members);
            }

            members.Add(objects[index]);
        }

        return
        [
            .. groups.Select(group => CreatePropertyNode(group.Key, group.Value))
                .OrderBy(node => node.Id, StringComparer.Ordinal)
        ];
    }

    private static DrawingValue? RoundGroupingValue(DrawingValue? value, DrawingPrecision precision)
    {
        if (value is not DrawingNumberValue number)
            return value;

        var rounded = precision.Round(number.Value, number.Unit);

        if (!rounded.IsFinite())
            return null;

        return number with {Value = number.Unit == DrawingUnit.Angle ? rounded * (Math.PI / 180) : rounded};
    }

    private static LensNode CreatePropertyNode(PropertyGroupKey key, List<LensNode> objects)
    {
        var first = objects[0];

        return new LensNode(
            key.GetIdentity(),
            string.Empty,
            [.. objects.SelectMany(node => node.Objects)],
            [.. objects],
            [
                .. key.Properties.Select(property => new DetailField(
                    DrawingProperties.GetLabel(property.Id),
                    string.Empty,
                    DetailValueKind.TypedValue,
                    property.Value,
                    PropertyKey: property.Id))
            ],
            [LensAction.Focus],
            LensNodeKind.PropertyGroup,
            first.TypeKey,
            key.Properties,
            GetCommonMetric(objects));
    }

    private static DrawingMetric? GetCommonMetric(IEnumerable<LensNode> objects)
    {
        var ids = objects.Select(node => node.RowMetric?.Id).Where(id => id is not null).Distinct().ToList();

        return ids.Count == 1 ? new DrawingMetric(ids[0]!.Value, null) : null;
    }

    private sealed class PropertyGroupKey(ImmutableArray<DrawingProperty> properties) : IEquatable<PropertyGroupKey>
    {
        public ImmutableArray<DrawingProperty> Properties { get; } = properties;

        public bool Equals(PropertyGroupKey? other) => other is not null && Properties.SequenceEqual(other.Properties);

        public override bool Equals(object? obj) => obj is PropertyGroupKey other && Equals(other);

        public override int GetHashCode() =>
            Properties.Aggregate(17, (hash, property) => unchecked(hash * 31 + property.GetHashCode()));

        public string GetIdentity()
        {
            var identity = string.Join(
                ";",
                Properties.Select(property => $"{SerializeKey(property.Id)}:{Serialize(property.Value)}"));

            var bytes = Encoding.UTF8.GetBytes(identity);
#if NETFRAMEWORK
            using var sha = SHA256.Create();
            var hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty);
#else
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
#endif

            return $"properties:{hash}";
        }

        private static string SerializeKey(DrawingPropertyKey key) => key.BuiltIn is { } id
            ? ((int) id).ToString(CultureInfo.InvariantCulture)
            : $"{key.Source}:{Encode(key.Name)}";

        private static string Serialize(DrawingValue? value) => value switch
        {
            null => "missing",
            DrawingTextValue text => $"text:{text.IsApplicationText}:{Encode(text.Text)}",
            DrawingNumberValue number =>
                $"number:{(int) number.Unit}:{number.Value.ToString("R", CultureInfo.InvariantCulture)}",
            DrawingBooleanValue boolean => $"bool:{boolean.Value}",
            DrawingLayerValue layer => $"layer:{Encode(layer.Id.DisplayId)}",
            DrawingColorValue color =>
                $"color:{(int) color.Color.Kind}:{color.Color.Value}:{EncodeOptional(color.Color.Name)}:{EncodeOptional(color.Color.BookName)}",
            DrawingLineweightValue lineweight =>
                $"lineweight:{(int) lineweight.Lineweight.Kind}:{lineweight.Lineweight.HundredthsOfMillimeter}",
            DrawingTransparencyValue transparency =>
                $"transparency:{(int) transparency.Transparency.Kind}:{transparency.Transparency.Alpha}",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown drawing value.")
        };

        private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

        private static string EncodeOptional(string? value) => value is null ? "null" : $"value:{Encode(value)}";
    }

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
