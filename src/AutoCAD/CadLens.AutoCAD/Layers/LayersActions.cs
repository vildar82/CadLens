using System.Collections.Immutable;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using CadLens.Lenses;
using CadLens.UI;
using Common;
using Common.AutoCAD;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

namespace CadLens.AutoCAD;

internal sealed class LayersActions(
    ILayersProvider lens,
    IEntityHighlightActions highlights,
    IEntityHighlightService graphics,
    IHostTaskService hostTasks) : ILayersActions
{
    public void ClearImmediately(bool redraw) => graphics.Clear(redraw);

    public Task<HostResult<LayersPresentation>> ReadAsync(IReadOnlySet<string> enabledFilters, CancellationToken cancellationToken) =>
        lens.LoadAsync(enabledFilters, cancellationToken);

    public async Task<string> EmphasizeAsync(CancellationToken cancellationToken)
    {
        var result = await highlights.EmphasizeSelectionAsync(cancellationToken);

        return result.Match(
            count => $"Highlight requested for {count} objects. Use Clear highlight to restore normal appearance.",
            reason => reason);
    }

    public Task<HostResult<bool>> ClearAsync(CancellationToken cancellationToken) =>
        highlights.ClearAsync(cancellationToken);

    public async Task<string> EmphasizeObjectsAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken)
    {
        if (!TryGetNativeIds(objects, out var targets))
            return "The targets do not belong to AutoCAD.";

        var result = await highlights.EmphasizeAsync(targets, cancellationToken);

        return result.Match(
            _ => objects.IsEmpty ? "Temporary effects cleared." : "Selection highlighted. Hidden objects remain hidden.",
            reason => reason);
    }

    public async Task<string> FocusAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken)
    {
        var result = await FocusHostAsync(objects, cancellationToken);

        return result.Match(_ => "View fitted to the available target bounds.", reason => reason);
    }

    private async Task<HostResult<bool>> FocusHostAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken)
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

            bounds = ReadBounds(database, objects);
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

    private static Extents3d? ReadBounds(Database database, ObjectId[] objects)
    {
        Extents3d? bounds = null;

        foreach (var target in objects)
        {
            if (!target.IsValid || target.Database != database)
                continue;

            var entity = target.GetObject<Entity>();

            if (entity is null || entity.OwnerId != database.CurrentSpaceId)
                continue;

            try
            {
                var extents = entity.GeometricExtents;

                if (!HasUsableBounds(extents))
                    continue;

                var combined = bounds ?? extents;
                combined.AddExtents(extents);
                bounds = combined;
            }
            catch (Exception exception) when (
                exception.ErrorStatus is ErrorStatus.NullExtents or ErrorStatus.InvalidExtents or ErrorStatus.NotApplicable)
            {
                // Bounds are optional for custom or empty entities; other native failures reach the queue.
            }
        }

        return bounds;
    }

    private static bool HasUsableBounds(Extents3d bounds) =>
        double.IsFinite(bounds.MinPoint.X) && double.IsFinite(bounds.MinPoint.Y) && double.IsFinite(bounds.MinPoint.Z) &&
        double.IsFinite(bounds.MaxPoint.X) && double.IsFinite(bounds.MaxPoint.Y) && double.IsFinite(bounds.MaxPoint.Z) &&
        bounds.MinPoint.X <= bounds.MaxPoint.X && bounds.MinPoint.Y <= bounds.MaxPoint.Y && bounds.MinPoint.Z <= bounds.MaxPoint.Z;

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