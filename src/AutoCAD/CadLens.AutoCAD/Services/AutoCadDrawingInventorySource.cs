using System.Collections.Immutable;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
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
        int? maximumObjects = null,
        bool includeBlockContents = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var document = Application.DocumentManager.MdiActiveDocument;

        if (document is null)
            return new HostResult<DrawingInventory>.Unavailable("The active drawing is no longer available.");

        var spaceId = document.Database.CurrentSpaceId;

        var result = await hostTasks.RunAsync(
            () => Read(document, spaceId, selectedObjects, maximumObjects, includeBlockContents, cancellationToken),
            cancellationToken);

        return result.Bind(inventory => inventory);
    }

    private static HostResult<DrawingInventory> Read(
        Document document,
        ObjectId spaceId,
        ImmutableArray<IPlacedObjectId>? selectedObjects,
        int? maximumObjects,
        bool includeBlockContents,
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
        var reader = new EntityReader(selectedObjects is null ? maximumObjects : null, cancellationToken);
        var entities = reader.Read(
            ReadEntities(database, space, selectedObjects, cancellationToken),
            includeBlockContents);

        if (entities is null)
            return new HostResult<DrawingInventory>.Unavailable(
                "Large drawing. Use Refresh to load all objects, or choose Selected objects.");

        var spaceLabel = space.IsLayout ? space.LayoutId.GetObject<Layout>()!.LayoutName : space.Name;
        var precision = new DrawingPrecision(database.Luprec, database.Auprec);

        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();

        return new HostResult<DrawingInventory>.Success(
            new DrawingInventory(spaceLabel, layers, entities.Value, Precision: precision));
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
            ? selected.OfType<EntityId>().Select(id => id.RootId).Distinct()
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

    private sealed class EntityReader(int? maximumObjects, CancellationToken cancellationToken)
    {
        private readonly AutoCadEntitySnapshotReader _snapshots = new(cancellationToken);
        private readonly List<EntitySnapshot> _entities = [];
        private readonly HashSet<ObjectId> _definitions = [];
        private int _visitedObjects;

        internal ImmutableArray<EntitySnapshot>? Read(IEnumerable<Entity> entities, bool includeBlockContents)
            => entities.All(entity => Add(entity, null, [], Matrix3d.Identity, null, includeBlockContents))
                ? [.. _entities]
                : null;

        private bool Add(
            Entity entity,
            EntityId? container,
            ImmutableArray<ILayerId> containerLayers,
            Matrix3d transform,
            int? inheritedColor,
            bool includeBlockContents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (container is not null && (!entity.Visible ||
                entity is AttributeReference {Invisible: true} or AttributeDefinition {Invisible: true}))
                return true;

            if (maximumObjects is { } limit && ++_visitedObjects > limit)
                return false;

            var id = new EntityId(entity.ObjectId, container);
            var layerId = containerLayers.LastOrDefault() is LayerId containingLayer &&
                          entity.LayerId.GetObject<LayerTableRecord>()?.Name == "0"
                ? containingLayer.NativeId
                : entity.LayerId;
            var displayColor = includeBlockContents ? ReadDisplayColor(entity, layerId, inheritedColor) : null;

            if (includeBlockContents && entity is BlockReference block and not MInsertBlock &&
                block.ExtensionDictionary.GetObject<DBDictionary>()?.Contains("ACAD_FILTER") is not true &&
                block.BlockTableRecord.GetObject<BlockTableRecord>() is
                    {IsFromExternalReference: false, IsFromOverlayReference: false} definition &&
                _definitions.Add(definition.ObjectId))
            {
                try
                {
                    return AddBlockContents(
                        block,
                        definition,
                        id,
                        containerLayers.Add(new LayerId(layerId)),
                        transform,
                        displayColor);
                }
                finally
                {
                    _definitions.Remove(definition.ObjectId);
                }
            }

            var snapshot = container is null
                ? _snapshots.Read(entity)
                : _snapshots.ReadOccurrence(entity, id, layerId, transform);
            _entities.Add(
                container is null
                    ? snapshot
                    : snapshot with {DisplayColor = displayColor, ContainerLayers = containerLayers});

            return true;
        }

        private bool AddBlockContents(
            BlockReference block,
            BlockTableRecord definition,
            EntityId id,
            ImmutableArray<ILayerId> containerLayers,
            Matrix3d transform,
            int? displayColor)
        {
            if (!block.Visible)
                return true;

            var blockTransform = transform * block.BlockTransform;

            foreach (var childId in definition.Cast<ObjectId>())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (childId.GetObject<Entity>() is not { } child || child.OwnerId != definition.ObjectId ||
                    child is AttributeDefinition {Constant: false})
                    continue;

                if (!Add(child, id, containerLayers, blockTransform, displayColor, true))
                    return false;
            }

            foreach (ObjectId attributeId in block.AttributeCollection)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (attributeId.GetObject<AttributeReference>() is { } attribute &&
                    attribute.OwnerId == block.ObjectId &&
                    !Add(attribute, id, containerLayers, transform, displayColor, true))
                    return false;
            }

            return true;
        }

        private static int? ReadDisplayColor(Entity entity, ObjectId layerId, int? inheritedColor)
        {
            try
            {
                using var color = entity.Color;

                if (color.IsByBlock)
                    return inheritedColor;

                if (!color.IsByLayer)
                    return AutoCadEntitySnapshotReader.ReadDisplayColor(color);

                if (layerId.GetObject<LayerTableRecord>() is not { } layer)
                    return null;

                using var layerColor = layer.Color;

                return AutoCadEntitySnapshotReader.ReadDisplayColor(layerColor);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return null;
            }
        }
    }
}
