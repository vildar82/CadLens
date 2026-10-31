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
    private const int BackgroundBuildThreshold = 512;

    /// <summary>Option identity for globally or viewport-frozen layers.</summary>
    public const string IncludeFrozen = "include-frozen";

    /// <summary>Option identity for switched-off layers.</summary>
    public const string IncludeOff = "include-off";

    /// <summary>Option identity for recursively showing local block contents.</summary>
    public const string ShowBlockContents = "show-block-contents";

    private static readonly ImmutableArray<BooleanFilter> Filters =
    [
        new(
            IncludeFrozen,
            "Include frozen",
            "Include globally and viewport-frozen layers without revealing them.",
            IconRole.Snowflake),
        new(IncludeOff, "Include off", "Include switched-off layers without switching them on.", IconRole.Lightbulb)
    ];

    private static readonly ImmutableArray<BooleanFilter> LayerFilters =
    [
        .. Filters,
        new(
            ShowBlockContents,
            "Show block contents",
            "Show objects inside local blocks, including nested blocks, without changing the drawing.",
            IconRole.Group)
    ];

    /// <summary>
    /// Returns the inclusion options available before any drawing data is read.
    /// </summary>
    /// <param name="grouping">Root organization of the lens.</param>
    public static ImmutableArray<BooleanFilter> GetFilters(DrawingGrouping grouping) =>
        grouping == DrawingGrouping.Layers ? LayerFilters : Filters;

    /// <inheritdoc />
    public async Task<HostResult<LensPresentation>> LoadAsync(
        DrawingGrouping grouping,
        IReadOnlyCollection<string> enabledFilters,
        ImmutableArray<IPlacedObjectId>? selectedObjects,
        CancellationToken cancellationToken,
        int? maximumObjects = null,
        IProgress<DrawingReadProgress>? progress = null)
    {
        // Capture options before awaiting: later UI changes belong to the next request.
        var includeFrozen = enabledFilters.Contains(IncludeFrozen);
        var includeOff = enabledFilters.Contains(IncludeOff);
        var showBlockContents = grouping == DrawingGrouping.Layers && enabledFilters.Contains(ShowBlockContents);
        var result = await source.ReadAsync(
            selectedObjects,
            cancellationToken,
            maximumObjects,
            showBlockContents,
            progress).ConfigureAwait(false);

        if (result is HostResult<DrawingInventory>.Unavailable unavailable)
            return new HostResult<LensPresentation>.Unavailable(unavailable.Reason);

        var snapshot = ((HostResult<DrawingInventory>.Success) result).Value;
        var presentation = await BuildLoadedAsync(
                snapshot,
                grouping,
                includeFrozen,
                includeOff,
                progress,
                cancellationToken)
            .ConfigureAwait(false);

        return new HostResult<LensPresentation>.Success(presentation);
    }

    /// <inheritdoc />
    public async Task<HostResult<LensPresentation>> LoadBlockContentsAsync(
        IPlacedObjectId block,
        IReadOnlyCollection<string> enabledFilters,
        CancellationToken cancellationToken,
        IProgress<DrawingReadProgress>? progress = null)
    {
        var includeFrozen = enabledFilters.Contains(IncludeFrozen);
        var includeOff = enabledFilters.Contains(IncludeOff);
        var result = await source.ReadAsync([block], cancellationToken, includeBlockContents: true, progress: progress)
            .ConfigureAwait(false);

        if (result is HostResult<DrawingInventory>.Unavailable unavailable)
            return new HostResult<LensPresentation>.Unavailable(unavailable.Reason);

        var snapshot = ((HostResult<DrawingInventory>.Success) result).Value;

        foreach (var entity in snapshot.Entities)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entity.Id.Equals(block))
                return new HostResult<LensPresentation>.Unavailable(
                    "Block contents are no longer available. Refresh to try again.");
        }

        var presentation = await BuildLoadedAsync(
            snapshot,
            DrawingGrouping.ObjectTypes,
            includeFrozen,
            includeOff,
            progress,
            cancellationToken).ConfigureAwait(false);

        return new HostResult<LensPresentation>.Success(presentation);
    }

    private static Task<LensPresentation> BuildLoadedAsync(
        DrawingInventory snapshot,
        DrawingGrouping grouping,
        bool includeFrozen,
        bool includeOff,
        IProgress<DrawingReadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var building = progress is null ? null : new ReadBuildProgress(progress);

        return snapshot.Entities.Length > BackgroundBuildThreshold
            ? Task.Run(
                () => Build(snapshot, grouping, includeFrozen, includeOff, null, null, cancellationToken, building),
                cancellationToken)
            : Task.FromResult(
                Build(snapshot, grouping, includeFrozen, includeOff, null, null, cancellationToken, building));
    }

    /// <summary>Rebuilds a drawing lens from detached facts without performing another host read.</summary>
    /// <param name="snapshot">Retained active-space facts.</param>
    /// <param name="grouping">Root grouping of the lens.</param>
    /// <param name="enabledFilters">Current visibility inclusion choices.</param>
    /// <param name="propertyGrouping">Selected combined properties for each runtime type.</param>
    /// <param name="propertyFilters">Optional property condition for each runtime type.</param>
    /// <param name="cancellationToken">Cancellation of detached filtering and tree construction.</param>
    /// <param name="progress">Optional progress through entities considered for the tree.</param>
    public static LensPresentation Build(
        DrawingInventory snapshot,
        DrawingGrouping grouping,
        IReadOnlyCollection<string> enabledFilters,
        IReadOnlyDictionary<string, ImmutableArray<DrawingPropertyKey>>? propertyGrouping = null,
        IReadOnlyDictionary<string, DrawingPropertyFilter>? propertyFilters = null,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Build(
            snapshot,
            grouping,
            enabledFilters.Contains(IncludeFrozen),
            enabledFilters.Contains(IncludeOff),
            propertyGrouping,
            propertyFilters,
            cancellationToken,
            progress);

    private static LensPresentation Build(
        DrawingInventory snapshot,
        DrawingGrouping grouping,
        bool includeFrozen,
        bool includeOff,
        IReadOnlyDictionary<string, ImmutableArray<DrawingPropertyKey>>? propertyGrouping,
        IReadOnlyDictionary<string, DrawingPropertyFilter>? propertyFilters,
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress)
    {
        var building = new BuildProgress(snapshot.Entities.Length, progress, cancellationToken);
        building.Start();

        var layers = snapshot.Layers
            .Where(layer =>
            {
                building.ThrowIfCancellationRequested();
                return IsIncluded(layer, includeFrozen, includeOff);
            })
            .ToDictionary(layer => layer.Id);
        var entities = new List<EntitySnapshot>();

        foreach (var entity in snapshot.Entities)
        {
            building.ThrowIfCancellationRequested();

            if (layers.ContainsKey(entity.LayerId) &&
                (entity.ContainerLayers.IsDefaultOrEmpty || entity.ContainerLayers.All(layers.ContainsKey)))
                entities.Add(entity);
            else
                building.Advance();
        }

        var precision = snapshot.Precision ?? DrawingPrecision.Default;
        var groups = grouping switch
        {
            DrawingGrouping.Layers => CreateLayerGroups(
                entities,
                layers,
                propertyGrouping,
                propertyFilters,
                precision,
                building),
            DrawingGrouping.ObjectTypes => CreateTypeGroups(
                entities,
                layers,
                null,
                propertyGrouping,
                propertyFilters,
                precision,
                building),
            _ => throw new ArgumentOutOfRangeException(nameof(grouping), grouping, "Unknown drawing grouping.")
        };
        var isLayers = grouping == DrawingGrouping.Layers;
        var presentation = new LensPresentation(
            isLayers ? "Layers" : "Object Types",
            snapshot.SpaceLabel,
            groups,
            GetFilters(grouping),
            isLayers
                ? "No layers contain included objects in the current space."
                : "No types contain included objects in the current space.",
            isLayers ? "All layers" : "All types",
            isLayers ? "Search layers" : "Search types",
            isLayers ? "layers" : "types",
            snapshot,
            snapshot.Precision);

        building.Complete();
        return presentation;
    }

    private static ImmutableArray<LensNode> CreateLayerGroups(
        IEnumerable<EntitySnapshot> entities,
        Dictionary<ILayerId, LayerSnapshot> layers,
        IReadOnlyDictionary<string, ImmutableArray<DrawingPropertyKey>>? propertyGrouping,
        IReadOnlyDictionary<string, DrawingPropertyFilter>? propertyFilters,
        DrawingPrecision precision,
        BuildProgress progress)
    {
        var entitiesByLayer = CheckItems(entities, progress).GroupBy(entity => entity.LayerId)
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
                progress))
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
        BuildProgress progress)
    {
        progress.ThrowIfCancellationRequested();

        var types = CreateTypeGroups(entities, layers, layer, propertyGrouping, propertyFilters, precision, progress);

        return new LensNode(
            layer.Id.DisplayId,
            layer.Name,
            [.. CheckItems(types, progress).SelectMany(type => type.Objects)],
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
        BuildProgress progress) =>
    [
        .. CheckItems(entities, progress).GroupBy(entity => entity.TypeKey, StringComparer.Ordinal)
            .OrderBy(group => group.Key.GetTypeLabel(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => CreateTypeNode(
                group,
                layers,
                layer,
                propertyGrouping,
                propertyFilters,
                precision,
                progress))
    ];

    private static LensNode CreateTypeNode(
        IGrouping<string, EntitySnapshot> entities,
        Dictionary<ILayerId, LayerSnapshot> layers,
        LayerSnapshot? layer,
        IReadOnlyDictionary<string, ImmutableArray<DrawingPropertyKey>>? propertyGrouping,
        IReadOnlyDictionary<string, DrawingPropertyFilter>? propertyFilters,
        DrawingPrecision precision,
        BuildProgress progress)
    {
        var label = entities.Key.GetTypeLabel();
        var filter = propertyFilters?.TryGetValue(entities.Key, out var selectedFilter) == true ? selectedFilter : null;
        var included = new List<EntitySnapshot>();

        foreach (var entity in entities)
        {
            progress.ThrowIfCancellationRequested();

            if (filter is null || filter.Matches(entity, layers[entity.LayerId], precision))
                included.Add(entity);
            else
                progress.Advance();
        }

        List<EntitySnapshot> orderedEntities =
        [
            .. CheckItems(included, progress).OrderBy(entity => entity.Id.DisplayId, StringComparer.Ordinal)
        ];
        var objects = orderedEntities
            .Select(entity => CreateObjectNode(layers[entity.LayerId], entity, label, progress))
            .ToImmutableArray();
        var fields = GetGroupingFields(orderedEntities, propertyGrouping, progress);
        var children = fields.IsEmpty
            ? objects
            : CreatePropertyGroups(orderedEntities, objects, layers, fields, precision, progress);
        progress.ThrowIfCancellationRequested();
        var metric = GetCommonMetric(objects, progress);

        return new LensNode(
            entities.Key,
            label,
            [.. CheckItems(objects, progress).SelectMany(node => node.Objects)],
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
        BuildProgress progress)
    {
        progress.ThrowIfCancellationRequested();

        var name = ReadName(DrawingPropertyId.BlockName) ?? ReadName(DrawingPropertyId.Name) ?? typeLabel;
        var node = new LensNode(
            entity.Id.DisplayId,
            $"{name} {entity.Id.DisplayId}",
            [entity.Id],
            [],
            [
                .. CreateLayerDetails(layer),
                CreatePrimitiveTypeField(entity.TypeKey),
                .. DrawingProperties.GetDetails(entity, layer),
                .. CreateAttributeDetails(entity, progress)
            ],
            entity.CanBrowseContents ? [LensAction.Focus, LensAction.BrowseContents] : [LensAction.Focus],
            LensNodeKind.Object,
            entity.TypeKey,
            [
                .. DrawingProperties.GetAvailableFields([entity]).Select(id =>
                    new DrawingProperty(id, DrawingProperties.GetValue(entity, layer, id)))
            ],
            RowMetric: DrawingProperties.GetPrimaryMetric(entity),
            DisplayColor: entity.Properties?.GetValueOrDefault(DrawingPropertyId.Color) is DrawingColorValue
            {
                Color.Kind: AssignedColorKind.ByLayer
            }
                ? layer.DisplayColor
                : entity.DisplayColor);

        progress.Advance();
        return node;

        string? ReadName(DrawingPropertyId id) =>
            DrawingProperties.GetValue(entity, layer, id) is DrawingTextValue {Text.Length: > 0} value
                ? value.Text
                : null;
    }

    private static IEnumerable<DetailField> CreateAttributeDetails(EntitySnapshot entity, BuildProgress progress)
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
            attributes.Count != 0 ? "" :
            count is DrawingNumberValue {Value: 0} ? "No attached attributes" : "Unavailable",
            DetailValueKind.ApplicationText);

        foreach (var (key, value) in attributes)
        {
            progress.ThrowIfCancellationRequested();
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
        IReadOnlyDictionary<string, ImmutableArray<DrawingPropertyKey>>? propertyGrouping,
        BuildProgress progress)
    {
        if (entities.Count == 0 || propertyGrouping is null ||
            !propertyGrouping.TryGetValue(entities[0].TypeKey, out var selected) ||
            selected.IsDefaultOrEmpty)
            return [];

        var available = DrawingProperties.GetAvailableFields(CheckItems(entities, progress));

        return [.. selected.Where(available.Contains).Distinct().OrderBy(id => id)];
    }

    private static ImmutableArray<LensNode> CreatePropertyGroups(
        List<EntitySnapshot> entities,
        ImmutableArray<LensNode> objects,
        Dictionary<ILayerId, LayerSnapshot> layers,
        ImmutableArray<DrawingPropertyKey> fields,
        DrawingPrecision precision,
        BuildProgress progress)
    {
        var groups = new Dictionary<PropertyGroupKey, List<LensNode>>();

        for (var index = 0; index < entities.Count; index++)
        {
            progress.ThrowIfCancellationRequested();
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
            .. CheckItems(groups, progress).Select(group => CreatePropertyNode(group.Key, group.Value, progress))
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

    private static LensNode CreatePropertyNode(PropertyGroupKey key, List<LensNode> objects, BuildProgress progress)
    {
        var first = objects[0];

        return new LensNode(
            key.GetIdentity(),
            string.Empty,
            [.. CheckItems(objects, progress).SelectMany(node => node.Objects)],
            [.. CheckItems(objects, progress)],
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
            GetCommonMetric(objects, progress));
    }

    private static DrawingMetric? GetCommonMetric(IEnumerable<LensNode> objects, BuildProgress progress)
    {
        var ids = CheckItems(objects, progress)
            .Select(node => node.RowMetric?.Id).Where(id => id is not null).Distinct().ToList();

        return ids.Count == 1 ? new DrawingMetric(ids[0]!.Value, null) : null;
    }

    private static IEnumerable<T> CheckItems<T>(IEnumerable<T> items, BuildProgress progress)
    {
        foreach (var item in items)
        {
            progress.ThrowIfCancellationRequested();
            yield return item;
        }

        progress.ThrowIfCancellationRequested();
    }

    private sealed class ReadBuildProgress(IProgress<DrawingReadProgress> progress) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) =>
            progress.Report(new DrawingReadProgress(value.Completed, value.Total, value.Completed, value.Stage));
    }

    private sealed class BuildProgress(
        int total,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        private const int ReportInterval = 256;
        private int _completed;

        internal void Start()
        {
            ThrowIfCancellationRequested();
            progress?.Report(new OperationProgress(OperationStage.Building, Total: total));
            ThrowIfCancellationRequested();
        }

        internal void Advance()
        {
            ThrowIfCancellationRequested();
            _completed++;

            if (_completed < total && _completed % ReportInterval == 0)
                progress?.Report(new OperationProgress(OperationStage.Building, _completed, total));

            ThrowIfCancellationRequested();
        }

        internal void Complete()
        {
            ThrowIfCancellationRequested();
            progress?.Report(new OperationProgress(OperationStage.Building, total, total));
            ThrowIfCancellationRequested();
        }

        internal void ThrowIfCancellationRequested() => cancellationToken.ThrowIfCancellationRequested();
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
