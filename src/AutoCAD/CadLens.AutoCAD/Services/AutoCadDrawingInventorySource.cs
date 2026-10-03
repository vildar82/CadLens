using System.Collections.Immutable;
using Autodesk.AutoCAD.DatabaseServices;
using CadLens.Lenses;
using Common;
using Common.AutoCAD;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadLens.AutoCAD;

internal sealed class AutoCadDrawingInventorySource(IHostTaskService hostTasks) : IDrawingInventorySource
{
    public Task<HostResult<DrawingInventory>> ReadAsync(CancellationToken cancellationToken) =>
        hostTasks.RunAsync(() => Read(cancellationToken), cancellationToken);

    private static DrawingInventory Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var document = Application.DocumentManager.MdiActiveDocument;
        var database = document.Database;

        using var transaction = database.TransactionManager.StartTransaction();

        var space = database.GetActiveSpace();
        var frozenLayers = ReadViewportFrozenLayers();
        var layers = ReadLayers(database, frozenLayers, cancellationToken);
        var reader = new AutoCadEntitySnapshotReader(cancellationToken);

        var entities = space.GetObjects<Entity>()
            .Select(reader.Read)
            .ToImmutableArray();

        var spaceLabel = space.IsLayout ? space.LayoutId.GetObject<Layout>()!.LayoutName : space.Name;
        var precision = new DrawingPrecision(database.Luprec, database.Auprec);

        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();

        return new DrawingInventory(spaceLabel, layers, entities, Precision: precision);
    }

    private static ImmutableArray<LayerSnapshot> ReadLayers(
        Database database,
        HashSet<ObjectId> frozenLayers,
        CancellationToken cancellationToken)
    {
        var layers = new List<LayerSnapshot>();

        foreach (var layer in database.LayerTableId.GetObject<LayerTable>()!.GetObjects<LayerTableRecord>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            layers.Add(ReadLayer(layer, frozenLayers));
        }

        return [.. layers];
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
        new LayerId(layer.ObjectId),
        layer.Name,
        layer.IsOff,
        layer.IsFrozen,
        frozenLayers.Contains(layer.ObjectId),
        layer.IsLocked);
}
