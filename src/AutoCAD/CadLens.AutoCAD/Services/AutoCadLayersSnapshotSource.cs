using System.Collections.Immutable;
using Autodesk.AutoCAD.DatabaseServices;
using CadLens.Core;
using CadLens.Lenses;
using Common;
using Common.AutoCAD;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadLens.AutoCAD;

internal sealed class AutoCadLayersSnapshotSource(IHostTaskService hostTasks) : ILayersSnapshotSource
{
    public Task<HostResult<LayersSnapshot>> ReadAsync(CancellationToken cancellationToken) =>
        hostTasks.RunAsync(Read, cancellationToken);

    private static LayersSnapshot Read()
    {
        var document = Application.DocumentManager.MdiActiveDocument;
        var database = document.Database;

        using var transaction = database.TransactionManager.StartTransaction();

        var space = database.GetActiveSpace();
        var frozenLayers = ReadViewportFrozenLayers();
        var layers = database.LayerTableId.GetObject<LayerTable>()!
            .GetObjects<LayerTableRecord>()
            .Select(layer => ReadLayer(layer, frozenLayers))
            .ToImmutableArray();

        var entities = space.GetObjects<Entity>()
            .Select(entity => new EntitySnapshot(
                new HostObjectId(entity.ObjectId),
                entity.LayerId.Handle.ToString(),
                entity.GetRXClass().Name))
            .ToImmutableArray();

        var spaceLabel = space.IsLayout ? space.LayoutId.GetObject<Layout>()!.LayoutName : space.Name;

        transaction.Commit();

        return new LayersSnapshot(spaceLabel, layers, entities);
    }

    private static HashSet<ObjectId> ReadViewportFrozenLayers()
    {
        var document = Application.DocumentManager.MdiActiveDocument;

        if (document.Database.TileMode || Convert.ToInt32(Application.GetSystemVariable("CVPORT")) <= 1)
            return [];

        var viewport = document.Editor.CurrentViewportObjectId.GetObject<Viewport>();

        return viewport is null ? [] : [.. viewport.GetFrozenLayers().Cast<ObjectId>()];
    }

    private static LayerSnapshot ReadLayer(LayerTableRecord layer, HashSet<ObjectId> frozenLayers) => new(
        layer.ObjectId.Handle.ToString(),
        layer.Name,
        layer.IsOff,
        layer.IsFrozen,
        frozenLayers.Contains(layer.ObjectId),
        layer.IsLocked);
}