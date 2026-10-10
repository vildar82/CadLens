using System.Collections.Immutable;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using CadLens.Lenses;
using CadLens.Common;
using CadLens.Common.AutoCAD;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadLens.AutoCAD;

internal sealed class AutoCadDrawingInventorySource(IHostTaskService hostTasks) : IDrawingInventorySource
{
    public async Task<HostResult<DrawingInventory>> ReadAsync(
        ImmutableArray<IPlacedObjectId>? selectedObjects,
        CancellationToken cancellationToken,
        int? maximumObjects = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var document = Application.DocumentManager.MdiActiveDocument;

        if (document is null)
            return new HostResult<DrawingInventory>.Unavailable("The active drawing is no longer available.");

        var spaceId = document.Database.CurrentSpaceId;

        var result = await hostTasks.RunAsync(
            () => Read(document, spaceId, selectedObjects, maximumObjects, cancellationToken),
            cancellationToken);

        return result.Bind(inventory => inventory);
    }

    private static HostResult<DrawingInventory> Read(
        Document document,
        ObjectId spaceId,
        ImmutableArray<IPlacedObjectId>? selectedObjects,
        int? maximumObjects,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var database = document.Database;

        if (document != Application.DocumentManager.MdiActiveDocument || database.CurrentSpaceId != spaceId)
            throw new InvalidOperationException("The active drawing space has changed. Refresh to try again.");

        using var transaction = database.TransactionManager.StartTransaction();

        var space = database.GetActiveSpace();

        if (selectedObjects is null && maximumObjects is { } limit && ExceedsLimit(space, limit, cancellationToken))
            return new HostResult<DrawingInventory>.Unavailable(
                "Large drawing. Use Refresh to load all objects, or choose Selected objects.");

        var frozenLayers = ReadViewportFrozenLayers(document);
        var layers = ReadLayers(database, frozenLayers, cancellationToken);
        var reader = new AutoCadEntitySnapshotReader(cancellationToken);

        var entities = ReadEntities(database, space, selectedObjects, cancellationToken)
            .Select(reader.Read)
            .ToImmutableArray();

        var spaceLabel = space.IsLayout ? space.LayoutId.GetObject<Layout>()!.LayoutName : space.Name;
        var precision = new DrawingPrecision(database.Luprec, database.Auprec);

        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();

        return new HostResult<DrawingInventory>.Success(
            new DrawingInventory(spaceLabel, layers, entities, Precision: precision));
    }

    private static bool ExceedsLimit(BlockTableRecord space, int limit, CancellationToken cancellationToken)
    {
        var count = 0;

        foreach (var id in space.Cast<ObjectId>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (id.IsValid && ++count > limit)
                return true;
        }

        return false;
    }

    private static IEnumerable<Entity> ReadEntities(
        Database database,
        BlockTableRecord space,
        ImmutableArray<IPlacedObjectId>? selectedObjects,
        CancellationToken cancellationToken)
    {
        var ids = selectedObjects is { } selected
            ? selected.OfType<EntityId>().Select(id => id.NativeId).Distinct()
            : space.Cast<ObjectId>();

        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!id.IsValid || id.Database != database)
                continue;

            if (id.GetObject<Entity>() is { } entity && entity.OwnerId == space.ObjectId)
                yield return entity;
        }
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

    private static HashSet<ObjectId> ReadViewportFrozenLayers(Document document)
    {
        if (document.Database.TileMode || Convert.ToInt32(Application.GetSystemVariable("CVPORT")) <= 1)
            return [];

        var viewport = document.Editor.CurrentViewportObjectId.GetObject<Viewport>();

        return viewport is null ? [] : [.. viewport.GetFrozenLayers().Cast<ObjectId>()];
    }

    private static LayerSnapshot ReadLayer(LayerTableRecord layer, HashSet<ObjectId> frozenLayers)
    {
        using var color = layer.Color;

        return new LayerSnapshot(
            new LayerId(layer.ObjectId),
            layer.Name,
            layer.IsOff,
            layer.IsFrozen,
            frozenLayers.Contains(layer.ObjectId),
            layer.IsLocked,
            AutoCadEntitySnapshotReader.ReadDisplayColor(color));
    }
}
