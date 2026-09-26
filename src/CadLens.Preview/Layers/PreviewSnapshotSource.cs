using CadLens.Lenses;
using Common;

namespace CadLens.Preview;

internal sealed class PreviewSnapshotSource(bool empty, bool unavailable) : ILayersSnapshotSource
{
    private static readonly (string Name, int Count)[] VisibleLayers =
    [
        ("C-ROAD-EDGE", 248),
        ("C-ROAD-CENTRELINE", 312),
        ("C-SITE-BOUNDARY", 186),
        ("C-LANDSCAPE-TREES", 144),
        ("C-PAVING-PATTERN", 128),
        ("C-DRAINAGE-STRUCTURES", 96),
        ("C-UTILITIES-EXISTING-WATER-SUPPLY-EXTERNAL-NETWORK-NORTH-BOUNDARY", 82),
        ("C-ANNOTATION", 52)
    ];

    private static readonly string[] EntityTypes = ["AcDbPolyline", "AcDbLine", "AcDbBlockReference"];
    private const int HiddenLayerObjectCount = 12;

    /// <inheritdoc />
    public Task<HostResult<LayersSnapshot>> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        HostResult<LayersSnapshot> result = unavailable
            ? new HostResult<LayersSnapshot>.Unavailable("Preview: simulated drawing read failure.")
            : new HostResult<LayersSnapshot>.Success(CreateSnapshot(cancellationToken));

        return Task.FromResult(result);
    }

    private LayersSnapshot CreateSnapshot(CancellationToken cancellationToken)
    {
        var layers = new List<LayerSnapshot>();
        var entities = new List<EntitySnapshot>();

        if (empty)
            return new LayersSnapshot("Preview - empty model space", [], []);

        foreach (var (name, count) in VisibleLayers)
            AddLayer(name, count, false, false);

        AddLayer("C-SURVEY-FROZEN", HiddenLayerObjectCount, true, false);
        AddLayer("C-REFERENCE-OFF", HiddenLayerObjectCount, false, true);

        return new LayersSnapshot("Preview - sample model space", [.. layers], [.. entities]);

        void AddLayer(string name, int count, bool frozen, bool off)
        {
            var layerId = new LayerId(layers.Count + 1);
            layers.Add(new LayerSnapshot(layerId, name, off, frozen, false, false));

            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = new EntityId(entities.Count + 1);
                entities.Add(new EntitySnapshot(id, layerId, EntityTypes[index % EntityTypes.Length]));
            }
        }
    }
}
