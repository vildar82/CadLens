using System.Collections.Immutable;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadLens.Common.AutoCAD;

/// <summary>Visualizes placed objects in the active AutoCAD drawing.</summary>
public sealed class AutoCadObjectVisualizationService(
    IHostTaskService hostTasks,
    IEntityIsolationActions isolation) : IObjectVisualizationService
{
    /// <inheritdoc />
    public async Task<HostResult<bool>> SelectAsync(
        ImmutableArray<IPlacedObjectId> objects,
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(OperationStage.Preparing));
        var document = Application.DocumentManager.MdiActiveDocument;

        if (document is null)
            return new HostResult<bool>.Unavailable("No active drawing.");

        if (!TryGetEntityIds(objects, cancellationToken, out var targets))
            return new HostResult<bool>.Unavailable("The targets do not belong to AutoCAD.");

        var space = document.Database.CurrentSpaceId;
        var viewport = document.Editor.CurrentViewportObjectId;
        var viewportNumber = Convert.ToInt32(Application.GetSystemVariable("CVPORT"));
        var result = await hostTasks.RunAsync<HostResult<bool>>(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (document != Application.DocumentManager.MdiActiveDocument ||
                    space != document.Database.CurrentSpaceId || viewport != document.Editor.CurrentViewportObjectId ||
                    viewportNumber != Convert.ToInt32(Application.GetSystemVariable("CVPORT")))
                    return new HostResult<bool>.Unavailable("The drawing context changed. Refresh before selecting.");

                var roots = new HashSet<ObjectId>();
                progress?.Report(new OperationProgress(OperationStage.Selecting, 0, targets.Length));

                using (var transaction = document.Database.TransactionManager.StartTransaction())
                {
                    var frozenLayers = EntityOccurrence.ReadFrozenLayers(document.Database, cancellationToken);

                    for (var index = 0; index < targets.Length; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var target = targets[index];

                        if (target.Container is null ||
                            EntityOccurrence.Resolve(
                                document.Database,
                                target,
                                frozenLayers,
                                cancellationToken) is not null)
                            roots.Add(target.RootId);

                        Report(progress, OperationStage.Selecting, index + 1, targets.Length);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    transaction.Commit();
                }

                cancellationToken.ThrowIfCancellationRequested();
                var count = document.Editor.SelectObjects([.. roots], cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (count == 0 && targets.Length > 0)
                    return new HostResult<bool>.Unavailable(
                        "No valid current-space targets remain. Selection cleared; refresh the list.");

                progress?.Report(new OperationProgress(OperationStage.Selecting, targets.Length, targets.Length));
                return new HostResult<bool>.Success(true);
            },
            cancellationToken);

        return result.Bind(value => value);
    }

    /// <inheritdoc />
    public async Task<HostResult<bool>> IsolateAsync(
        ImmutableArray<IPlacedObjectId> objects,
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryGetEntityIds(objects, cancellationToken, out var targets))
            return new HostResult<bool>.Unavailable("The targets do not belong to AutoCAD.");

        var result = await isolation.IsolateAsync(targets, cancellationToken, progress);
        return result.Bind(_ => new HostResult<bool>.Success(true));
    }

    /// <inheritdoc />
    public async Task<HostResult<bool>> FocusAsync(
        ImmutableArray<IPlacedObjectId> objects,
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(OperationStage.Preparing));
        var document = Application.DocumentManager.MdiActiveDocument;

        if (document is null || objects.IsDefaultOrEmpty)
            return new HostResult<bool>.Unavailable("No active drawing or Focus targets.");

        if (!TryGetEntityIds(objects, cancellationToken, out var targets))
            return new HostResult<bool>.Unavailable("The targets do not belong to AutoCAD.");

        var space = document.Database.CurrentSpaceId;
        var viewport = document.Editor.CurrentViewportObjectId;
        var viewportNumber = Convert.ToInt32(Application.GetSystemVariable("CVPORT"));
        var result = await hostTasks.RunAsync(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (document != Application.DocumentManager.MdiActiveDocument ||
                    space != document.Database.CurrentSpaceId || viewport != document.Editor.CurrentViewportObjectId ||
                    viewportNumber != Convert.ToInt32(Application.GetSystemVariable("CVPORT")))
                    return new HostResult<bool>.Unavailable("The drawing context changed. Refresh before using Focus.");

                return Focus(document, targets, viewportNumber, cancellationToken, progress);
            },
            cancellationToken);

        return result.Bind(value => value);
    }

    private static HostResult<bool> Focus(
        Document document,
        EntityId[] objects,
        int viewportNumber,
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress)
    {
        var database = document.Database;
        var editor = document.Editor;
        Extents3d? bounds;
        var direct = new List<ObjectId>();
        var nested = new List<EntityId>();
        var seen = new HashSet<EntityId>();

        foreach (var target in objects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!seen.Add(target))
                continue;

            if (target.Container is null)
                direct.Add(target.NativeId);
            else
                nested.Add(target);
        }

        using (var transaction = database.TransactionManager.StartTransaction())
        {
            if (!database.TileMode && viewportNumber > 1)
            {
                var viewport = editor.CurrentViewportObjectId.GetObject<Viewport>();
                cancellationToken.ThrowIfCancellationRequested();

                if (viewport is null || viewport.Locked)
                    return new HostResult<bool>.Unavailable(
                        "Focus is unavailable in a locked or unavailable layout viewport.");
            }

            progress?.Report(new OperationProgress(OperationStage.Reading, 0, direct.Count));
            bounds = database.ReadBounds(direct, progress, cancellationToken);
            var frozenLayers = EntityOccurrence.ReadFrozenLayers(database, cancellationToken);
            var displayed = new VisibleBounds(cancellationToken);
            progress?.Report(new OperationProgress(OperationStage.Reading, 0, nested.Count));

            for (var index = 0; index < nested.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var occurrence = EntityOccurrence.Resolve(database, nested[index], frozenLayers, cancellationToken);

                if (occurrence?.ReadBounds(displayed) is { } extents)
                {
                    var combined = bounds ?? extents;
                    combined.AddExtents(extents);
                    bounds = combined;
                }

                Report(progress, OperationStage.Reading, index + 1, nested.Count);
            }

            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
        }

        if (bounds is null)
            return new HostResult<bool>.Unavailable(
                "No current-space targets have usable bounds. Refresh if objects were erased or moved.");

        cancellationToken.ThrowIfCancellationRequested();

        using (var view = editor.GetCurrentView())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (view.PerspectiveEnabled)
                return new HostResult<bool>.Unavailable("Focus is unavailable in perspective views.");
        }

        // Apply the view after the read transaction has finished.
        progress?.Report(new OperationProgress(OperationStage.Focusing, 0, 1));
        cancellationToken.ThrowIfCancellationRequested();
        editor.Zoom(database.ClipToExtents(bounds.Value, database.TileMode || viewportNumber > 1), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(OperationStage.Focusing, 1, 1));
        return new HostResult<bool>.Success(true);
    }

    private static bool TryGetEntityIds(
        ImmutableArray<IPlacedObjectId> objects,
        CancellationToken cancellationToken,
        out EntityId[] entityIds)
    {
        var ids = new List<EntityId>(objects.Length);

        foreach (var target in objects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (target is not EntityId entity)
            {
                entityIds = [];
                return false;
            }

            ids.Add(entity);
        }

        entityIds = [.. ids];
        cancellationToken.ThrowIfCancellationRequested();
        return true;
    }

    private static void Report(
        IProgress<OperationProgress>? progress,
        OperationStage stage,
        int completed,
        int total)
    {
        if (completed % 64 == 0 || completed == total)
            progress?.Report(new OperationProgress(stage, completed, total));
    }
}