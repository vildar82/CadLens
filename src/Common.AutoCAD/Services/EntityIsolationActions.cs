using Autodesk.AutoCAD.DatabaseServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace Common.AutoCAD;

/// <inheritdoc />
/// <param name="hostTasks">Queue used for native drawing access.</param>
/// <param name="graphics">Temporary graphics owned by the caller.</param>
public sealed class EntityIsolationActions(
    IHostTaskService hostTasks,
    IEntityIsolationService graphics) : IEntityIsolationActions
{
    /// <inheritdoc />
    public async Task<HostResult<int>> IsolateAsync(ObjectId[] objects, CancellationToken cancellationToken)
    {
        var document = Application.DocumentManager.MdiActiveDocument;

        if (document is null)
            return new HostResult<int>.Unavailable("No active drawing.");

        var space = document.Database.CurrentSpaceId;
        var viewport = document.Editor.CurrentViewportObjectId;
        var viewportNumber = Convert.ToInt32(Application.GetSystemVariable("CVPORT"));
        var result = await hostTasks.RunAsync(
            () =>
            {
                if (document != Application.DocumentManager.MdiActiveDocument ||
                    space != document.Database.CurrentSpaceId ||
                    viewport != document.Editor.CurrentViewportObjectId ||
                    viewportNumber != Convert.ToInt32(Application.GetSystemVariable("CVPORT")))
                    return new HostResult<int>.Unavailable("The drawing context changed. Refresh before isolating.");

                return IsolateSelection(objects);
            },
            cancellationToken);

        return result.Bind(value => value);
    }

    /// <inheritdoc />
    public Task<HostResult<bool>> ClearAsync(CancellationToken cancellationToken) =>
        hostTasks.RunAsync(
            () =>
            {
                graphics.Clear();

                return true;
            },
            cancellationToken);

    private HostResult<int> IsolateSelection(ObjectId[] selectedIds)
    {
        var document = Application.DocumentManager.MdiActiveDocument;

        if (selectedIds.Length == 0)
        {
            graphics.Clear();
            return new HostResult<int>.Success(0);
        }

        ObjectId[] inventory;

        using (var transaction = document.Database.TransactionManager.StartTransaction())
        {
            inventory = document.Database.GetActiveSpace().Cast<ObjectId>().ToArray();
            transaction.Commit();
        }

        var targets = selectedIds
            .Intersect(inventory)
            .Where(id => id is {IsValid: true, IsErased: false})
            .ToArray();

        if (targets.Length == 0)
        {
            graphics.Clear();
            return new HostResult<int>.Unavailable("The selection contains no direct active-space objects.");
        }

        graphics.Apply(document.Database, targets, inventory);

        return new HostResult<int>.Success(targets.Length);
    }
}
