using System.Collections.Immutable;
using Autodesk.AutoCAD.DatabaseServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace Common.AutoCAD;

/// <summary>Visualizes placed objects in the active AutoCAD drawing.</summary>
public sealed class AutoCadObjectVisualizationService(
    IHostTaskService hostTasks,
    IEntityIsolationActions isolation) : IObjectVisualizationService
{
    /// <inheritdoc />
    public async Task<HostResult<bool>> SelectAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken)
    {
        var document = Application.DocumentManager.MdiActiveDocument;

        if (document is null)
            return new HostResult<bool>.Unavailable("No active drawing.");

        if (!TryGetNativeIds(objects, out var targets))
            return new HostResult<bool>.Unavailable("The targets do not belong to AutoCAD.");

        var space = document.Database.CurrentSpaceId;
        var viewport = document.Editor.CurrentViewportObjectId;
        var viewportNumber = Convert.ToInt32(Application.GetSystemVariable("CVPORT"));
        var result = await hostTasks.RunAsync(
            () =>
            {
                if (document != Application.DocumentManager.MdiActiveDocument ||
                    space != document.Database.CurrentSpaceId || viewport != document.Editor.CurrentViewportObjectId ||
                    viewportNumber != Convert.ToInt32(Application.GetSystemVariable("CVPORT")))
                    return new HostResult<bool>.Unavailable("The drawing context changed. Refresh before selecting.");

                var count = document.Editor.SelectObjects(targets);

                if (count == 0 && targets.Length > 0)
                    return new HostResult<bool>.Unavailable("No valid current-space targets remain. Selection cleared; refresh the list.");

                return (HostResult<bool>)new HostResult<bool>.Success(true);
            },
            cancellationToken);

        return result.Bind(value => value);
    }

    /// <inheritdoc />
    public async Task<HostResult<bool>> IsolateAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken)
    {
        if (!TryGetNativeIds(objects, out var targets))
            return new HostResult<bool>.Unavailable("The targets do not belong to AutoCAD.");

        var result = await isolation.IsolateAsync(targets, cancellationToken);

        return result.Bind(_ => new HostResult<bool>.Success(true));
    }

    /// <inheritdoc />
    public async Task<HostResult<bool>> FocusAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken)
    {
        var document = Application.DocumentManager.MdiActiveDocument;

        if (document is null || objects.IsDefaultOrEmpty)
            return new HostResult<bool>.Unavailable("No active drawing or Focus targets.");

        if (!TryGetNativeIds(objects, out var targets))
            return new HostResult<bool>.Unavailable("The targets do not belong to AutoCAD.");

        var space = document.Database.CurrentSpaceId;
        var viewport = document.Editor.CurrentViewportObjectId;
        var viewportNumber = Convert.ToInt32(Application.GetSystemVariable("CVPORT"));
        var result = await hostTasks.RunAsync(
            () =>
            {
                if (document != Application.DocumentManager.MdiActiveDocument ||
                    space != document.Database.CurrentSpaceId || viewport != document.Editor.CurrentViewportObjectId ||
                    viewportNumber != Convert.ToInt32(Application.GetSystemVariable("CVPORT")))
                    return new HostResult<bool>.Unavailable("The drawing context changed. Refresh before using Focus.");

                return Focus(document.Database, targets, viewportNumber);
            },
            cancellationToken);

        return result.Bind(value => value);
    }

    private static HostResult<bool> Focus(Database database, ObjectId[] objects, int viewportNumber)
    {
        var editor = Application.DocumentManager.MdiActiveDocument.Editor;
        Extents3d? bounds;

        using (var transaction = database.TransactionManager.StartTransaction())
        {
            if (!database.TileMode && viewportNumber > 1)
            {
                var viewport = editor.CurrentViewportObjectId.GetObject<Viewport>();

                if (viewport is null || viewport.Locked)
                    return new HostResult<bool>.Unavailable("Focus is unavailable in a locked or unavailable layout viewport.");
            }

            bounds = database.ReadBounds(objects);
            transaction.Commit();
        }

        if (bounds is null)
            return new HostResult<bool>.Unavailable("No current-space targets have usable bounds. Refresh if objects were erased or moved.");

        using (var view = editor.GetCurrentView())
        {
            if (view.PerspectiveEnabled)
                return new HostResult<bool>.Unavailable("Focus is unavailable in perspective views.");
        }

        // Apply the view after the read transaction has finished.
        editor.Zoom(bounds.Value);

        return new HostResult<bool>.Success(true);
    }

    private static bool TryGetNativeIds(ImmutableArray<IPlacedObjectId> objects, out ObjectId[] nativeIds)
    {
        var ids = new List<ObjectId>(objects.Length);

        foreach (var target in objects)
        {
            if (target is not EntityId entity)
            {
                nativeIds = [];
                return false;
            }

            ids.Add(entity.NativeId);
        }

        nativeIds = [.. ids];
        return true;
    }
}
