using System.Collections.Immutable;
using System.Diagnostics;
using System.Windows.Threading;
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
    private const string LargeDrawingMessage =
        "Large drawing. Use Refresh to load all objects, or choose Selected objects.";

    public async Task<HostResult<DrawingInventory>> ReadAsync(
        ImmutableArray<IPlacedObjectId>? selectedObjects,
        CancellationToken cancellationToken,
        int? maximumObjects = null,
        bool includeBlockContents = false,
        IProgress<DrawingReadProgress>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var document = Application.DocumentManager.MdiActiveDocument;

        if (document is null)
            return new HostResult<DrawingInventory>.Unavailable("The active drawing is no longer available.");

        var spaceId = document.Database.CurrentSpaceId;
        var dispatcher = Dispatcher.FromThread(Thread.CurrentThread);
        ReadSession? session = null;

        try
        {
            // ReSharper disable once AccessToDisposedClosure -- Dispatcher cleanup waits for the native callback.
            var prepared = await hostTasks.RunAsync(
                () => session = Prepare(
                    document,
                    spaceId,
                    selectedObjects,
                    maximumObjects,
                    includeBlockContents,
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);

            if (prepared is HostResult<ReadSession?>.Unavailable unavailable)
                return new HostResult<DrawingInventory>.Unavailable(unavailable.Reason);

            if (session is null)
                return new HostResult<DrawingInventory>.Unavailable(LargeDrawingMessage);

            while (true)
            {
                progress?.Report(session.Progress);
                // Give the host dispatcher time to paint and accept cancellation between native callbacks.
                if (dispatcher is null)
                    await Task.Yield();
                else
                    await dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Background, cancellationToken);

                // ReSharper disable once AccessToDisposedClosure -- Each queued batch is awaited before cleanup.
                var batch = await hostTasks.RunAsync(() => session.ReadBatch(document, spaceId), cancellationToken)
                    .ConfigureAwait(false);

                if (batch is HostResult<bool>.Unavailable failure)
                    return new HostResult<DrawingInventory>.Unavailable(failure.Reason);

                if (!((HostResult<bool>.Success) batch).Value)
                    return new HostResult<DrawingInventory>.Unavailable(LargeDrawingMessage);

                if (!session.IsComplete)
                    continue;

                progress?.Report(session.Progress);
                return new HostResult<DrawingInventory>.Success(session.CreateInventory());
            }
        }
        finally
        {
            if (dispatcher is null)
                session?.Dispose();
            else
                await dispatcher.InvokeAsync(() => session?.Dispose(), DispatcherPriority.Background);
        }
    }

    private static ReadSession? Prepare(
        Document document,
        ObjectId spaceId,
        ImmutableArray<IPlacedObjectId>? selectedObjects,
        int? maximumObjects,
        bool includeBlockContents,
        CancellationToken cancellationToken)
    {
        ValidateContext(document, spaceId, cancellationToken);
        var database = document.Database;
        using var transaction = database.TransactionManager.StartTransaction();
        var space = database.GetActiveSpace();
        var roots = ReadRootIds(database, space, selectedObjects, maximumObjects, cancellationToken);

        if (roots is null)
            return null;

        var frozenLayers = ReadViewportFrozenLayers(document);
        var layers = ReadLayers(database, frozenLayers, cancellationToken);
        var spaceLabel = space.IsLayout ? space.LayoutId.GetObject<Layout>()!.LayoutName : space.Name;
        var precision = new DrawingPrecision(database.Luprec, database.Auprec);
        transaction.Commit();

        return new ReadSession(
            database,
            roots.Value,
            layers,
            spaceLabel,
            precision,
            selectedObjects is null ? maximumObjects : null,
            includeBlockContents,
            cancellationToken);
    }

    private static ImmutableArray<ObjectId>? ReadRootIds(
        Database database,
        BlockTableRecord space,
        ImmutableArray<IPlacedObjectId>? selectedObjects,
        int? maximumObjects,
        CancellationToken cancellationToken)
    {
        var ids = selectedObjects is { } selected
            ? selected.OfType<EntityId>().Select(id => id.RootId).Distinct()
            : space.Cast<ObjectId>();
        var roots = new List<ObjectId>();

        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!id.IsValid || id.IsErased || id.Database != database)
                continue;

            roots.Add(id);

            if (selectedObjects is null && maximumObjects is { } limit && roots.Count > limit)
                return null;
        }

        return [.. roots];
    }

    private static void ValidateContext(Document document, ObjectId spaceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (document != Application.DocumentManager.MdiActiveDocument || document.Database.CurrentSpaceId != spaceId)
            throw new InvalidOperationException("The active drawing space has changed. Refresh to try again.");
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

    private sealed class ReadSession : IDisposable
    {
        private const int ObjectsPerBatch = 128;
        private const int MillisecondsPerBatch = 20;
        private readonly Database _database;
        private readonly ImmutableArray<ObjectId> _roots;
        private readonly ImmutableArray<LayerSnapshot> _layers;
        private readonly Dictionary<ObjectId, LayerSnapshot> _layersById;
        private readonly string _spaceLabel;
        private readonly DrawingPrecision _precision;
        private readonly int? _maximumObjects;
        private readonly bool _includeBlockContents;
        private readonly CancellationToken _cancellationToken;
        private readonly AutoCadEntitySnapshotReader _snapshots;
        private readonly List<EntitySnapshot> _entities = [];
        private readonly Dictionary<ObjectId, ImmutableArray<ObjectId>> _definitionMembers = [];
        private readonly Dictionary<ObjectId, ObjectId?> _expandableDefinitions = [];
        private readonly HashSet<ObjectId> _activeDefinitions = [];
        private readonly Stack<TraversalFrame> _frames = [];
        private int _nextRoot;
        private int _completedRoots;
        private int _visitedObjects;
        private bool _checkingLimit;
        private bool _rootStarted;
        private bool _drawingChanged;

        internal ReadSession(
            Database database,
            ImmutableArray<ObjectId> roots,
            ImmutableArray<LayerSnapshot> layers,
            string spaceLabel,
            DrawingPrecision precision,
            int? maximumObjects,
            bool includeBlockContents,
            CancellationToken cancellationToken)
        {
            _database = database;
            _roots = roots;
            _layers = layers;
            _layersById = layers.ToDictionary(layer => ((LayerId) layer.Id).NativeId);
            _spaceLabel = spaceLabel;
            _precision = precision;
            _maximumObjects = maximumObjects;
            _includeBlockContents = includeBlockContents;
            _cancellationToken = cancellationToken;
            _snapshots = new AutoCadEntitySnapshotReader(cancellationToken);
            _checkingLimit = includeBlockContents && maximumObjects is not null;
            database.ObjectModified += OnObjectChanged;
            database.ObjectAppended += OnObjectChanged;
            database.ObjectErased += OnObjectErased;
        }

        internal bool IsComplete { get; private set; }

        internal DrawingReadProgress Progress => _checkingLimit
            ? new DrawingReadProgress(0, _roots.Length, 0)
            : new DrawingReadProgress(_completedRoots, _roots.Length, _visitedObjects);

        internal bool ReadBatch(Document document, ObjectId spaceId)
        {
            ValidateContext(document, spaceId, _cancellationToken);
            EnsureDrawingUnchanged();
            using var transaction = document.Database.TransactionManager.StartTransaction();
            var watch = Stopwatch.StartNew();

            for (var count = 0; count < ObjectsPerBatch && watch.ElapsedMilliseconds < MillisecondsPerBatch; count++)
            {
                if (!MoveToNextObject(spaceId, out var frame, out var objectId))
                {
                    CompletePass();
                    break;
                }

                if (!Visit(frame!, objectId))
                    return false;
            }

            _cancellationToken.ThrowIfCancellationRequested();
            EnsureDrawingUnchanged();
            transaction.Commit();
            return true;
        }

        internal DrawingInventory CreateInventory() =>
            new(_spaceLabel, _layers, [.. _entities], Precision: _precision);

        public void Dispose()
        {
            _database.ObjectModified -= OnObjectChanged;
            _database.ObjectAppended -= OnObjectChanged;
            _database.ObjectErased -= OnObjectErased;
        }

        private void OnObjectChanged(object? sender, ObjectEventArgs args) => _drawingChanged = true;

        private void OnObjectErased(object? sender, ObjectErasedEventArgs args) => _drawingChanged = true;

        private void EnsureDrawingUnchanged()
        {
            if (_drawingChanged)
                throw new InvalidOperationException("The drawing has changed. Refresh to try again.");
        }

        private bool MoveToNextObject(ObjectId spaceId, out TraversalFrame? frame, out ObjectId objectId)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            while (_frames.Count > 0 && _frames.Peek().Index == _frames.Peek().Ids.Length)
            {
                var completed = _frames.Pop();

                if (completed.DefinitionId is { } definition)
                    _activeDefinitions.Remove(definition);
            }

            if (_frames.Count == 0)
            {
                if (_rootStarted)
                    _completedRoots++;

                _rootStarted = false;

                if (_nextRoot == _roots.Length)
                {
                    frame = null;
                    objectId = default;
                    return false;
                }

                _frames.Push(new TraversalFrame([_roots[_nextRoot++]], spaceId));
                _rootStarted = true;
            }

            frame = _frames.Peek();
            objectId = frame.Ids[frame.Index++];
            return true;
        }

        private void CompletePass()
        {
            if (!_checkingLimit)
            {
                IsComplete = true;
                return;
            }

            // The automatic guard examines structure before any geometry or property getters run.
            _checkingLimit = false;
            _nextRoot = 0;
            _completedRoots = 0;
            _visitedObjects = 0;
        }

        private bool Visit(TraversalFrame frame, ObjectId objectId)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var entity = objectId.GetObject<Entity>();

            if (entity is null || entity.OwnerId != frame.OwnerId ||
                frame.Container is not null && (!entity.Visible ||
                                                entity is AttributeReference {Invisible: true} or AttributeDefinition
                                                {
                                                    Invisible: true
                                                }) ||
                frame.DefinitionId is not null && entity is AttributeDefinition {Constant: false})
                return true;

            _visitedObjects++;

            if (_checkingLimit && _maximumObjects is { } limit && _visitedObjects > limit)
                return false;

            var id = new EntityId(objectId, frame.Container);
            var layerId = ReadEffectiveLayer(entity.LayerId, frame.ContainerLayers);
            var displayColor = !_checkingLimit && _includeBlockContents
                ? ReadDisplayColor(entity, layerId, frame.InheritedColor)
                : null;
            var definition = GetExpandableDefinition(entity);

            if (_includeBlockContents && entity is BlockReference block && definition is { } definitionId &&
                _activeDefinitions.Add(definitionId))
            {
                return ExpandBlock(block, definitionId, id, layerId, displayColor, frame);
            }

            if (_checkingLimit)
                return true;

            var snapshot = frame.Container is null
                ? _snapshots.Read(entity) with {CanBrowseContents = definition is not null}
                : _snapshots.ReadOccurrence(entity, id, layerId, frame.Transform);
            _entities.Add(
                frame.Container is null
                    ? snapshot
                    : snapshot with {DisplayColor = displayColor, ContainerLayers = frame.ContainerLayers});
            return true;
        }

        private ObjectId ReadEffectiveLayer(ObjectId layerId, ImmutableArray<ILayerId> containerLayers) =>
            containerLayers.LastOrDefault() is LayerId containingLayer &&
            _layersById.TryGetValue(layerId, out var layer) && layer.Name == "0"
                ? containingLayer.NativeId
                : layerId;

        private ObjectId? GetExpandableDefinition(Entity entity)
        {
            if (entity is not BlockReference block || entity is MInsertBlock)
                return null;

            if (_expandableDefinitions.TryGetValue(entity.ObjectId, out var cached))
                return cached;

            var definition = block.ExtensionDictionary.GetObject<DBDictionary>()?.Contains("ACAD_FILTER") is not true &&
                             block.BlockTableRecord.GetObject<BlockTableRecord>() is
                                 {IsFromExternalReference: false, IsFromOverlayReference: false} record
                ? record.ObjectId
                : (ObjectId?) null;
            _expandableDefinitions.Add(entity.ObjectId, definition);
            return definition;
        }

        private bool ExpandBlock(
            BlockReference block,
            ObjectId definitionId,
            EntityId id,
            ObjectId layerId,
            int? displayColor,
            TraversalFrame parent)
        {
            if (!block.Visible)
            {
                _activeDefinitions.Remove(definitionId);
                return true;
            }

            var layers = parent.ContainerLayers.Add(new LayerId(layerId));
            var attributes = ReadIds(block.AttributeCollection.Cast<ObjectId>());

            if (!attributes.IsEmpty)
                _frames.Push(
                    new TraversalFrame(attributes, block.ObjectId, id, layers, parent.Transform, displayColor));

            if (!_definitionMembers.TryGetValue(definitionId, out var members))
            {
                var captured = ReadDefinitionMembers(definitionId);

                if (captured is null)
                    return false;

                members = captured.Value;
                _definitionMembers.Add(definitionId, members);
            }

            _frames.Push(
                new TraversalFrame(
                    members,
                    definitionId,
                    id,
                    layers,
                    parent.Transform * block.BlockTransform,
                    displayColor,
                    definitionId));
            return true;
        }

        private ImmutableArray<ObjectId> ReadIds(IEnumerable<ObjectId> ids)
        {
            var captured = new List<ObjectId>();

            foreach (var id in ids)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                captured.Add(id);
            }

            return [.. captured];
        }

        private ImmutableArray<ObjectId>? ReadDefinitionMembers(ObjectId definitionId)
        {
            var ids = definitionId.GetObject<BlockTableRecord>()!.Cast<ObjectId>();

            if (!_checkingLimit)
                return ReadIds(ids);

            var captured = new List<ObjectId>();

            foreach (var id in ids)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var entity = id.GetObject<Entity>();

                if (entity is null || entity.OwnerId != definitionId || !entity.Visible ||
                    entity is AttributeDefinition {Constant: false} or AttributeDefinition {Invisible: true} or
                        AttributeReference {Invisible: true})
                    continue;

                captured.Add(id);

                // Every visible direct member will consume one visit, even if it expands recursively.
                if (_maximumObjects is { } limit && captured.Count > limit - _visitedObjects)
                    return null;
            }

            return [.. captured];
        }

        private int? ReadDisplayColor(Entity entity, ObjectId layerId, int? inheritedColor)
        {
            try
            {
                using var color = entity.Color;

                if (color.IsByBlock)
                    return inheritedColor;

                if (!color.IsByLayer)
                    return AutoCadEntitySnapshotReader.ReadDisplayColor(color);

                return _layersById.TryGetValue(layerId, out var layer) ? layer.DisplayColor : null;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return null;
            }
        }
    }

    private sealed class TraversalFrame(
        ImmutableArray<ObjectId> ids,
        ObjectId ownerId,
        EntityId? container = null,
        ImmutableArray<ILayerId> containerLayers = default,
        Matrix3d? transform = null,
        int? inheritedColor = null,
        ObjectId? definitionId = null)
    {
        internal ImmutableArray<ObjectId> Ids { get; } = ids;
        internal ObjectId OwnerId { get; } = ownerId;
        internal EntityId? Container { get; } = container;
        internal ImmutableArray<ILayerId> ContainerLayers { get; } = containerLayers.IsDefault ? [] : containerLayers;
        internal Matrix3d Transform { get; } = transform ?? Matrix3d.Identity;
        internal int? InheritedColor { get; } = inheritedColor;
        internal ObjectId? DefinitionId { get; } = definitionId;
        internal int Index { get; set; }
    }
}
