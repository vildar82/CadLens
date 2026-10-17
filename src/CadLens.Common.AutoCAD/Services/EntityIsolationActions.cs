using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadLens.Common.AutoCAD;

/// <inheritdoc />
/// <param name="hostTasks">Queue used for native drawing access.</param>
/// <param name="graphics">Temporary graphics owned by the caller.</param>
public sealed class EntityIsolationActions(
    IHostTaskService hostTasks,
    IEntityIsolationService graphics) : IEntityIsolationActions
{
    /// <inheritdoc />
    public async Task<HostResult<int>> IsolateAsync(
        EntityId[] objects,
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(OperationStage.Preparing));
        var document = Application.DocumentManager.MdiActiveDocument;

        if (document is null)
            return new HostResult<int>.Unavailable("No active drawing.");

        var space = document.Database.CurrentSpaceId;
        var viewport = document.Editor.CurrentViewportObjectId;
        var viewportNumber = Convert.ToInt32(Application.GetSystemVariable("CVPORT"));
        var result = await hostTasks.RunAsync(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (document != Application.DocumentManager.MdiActiveDocument ||
                    space != document.Database.CurrentSpaceId ||
                    viewport != document.Editor.CurrentViewportObjectId ||
                    viewportNumber != Convert.ToInt32(Application.GetSystemVariable("CVPORT")))
                    return new HostResult<int>.Unavailable("The drawing context changed. Refresh before isolating.");

                return IsolateSelection(document, objects, cancellationToken, progress);
            },
            cancellationToken);

        return result.Bind(value => value);
    }

    /// <inheritdoc />
    public Task<HostResult<bool>> ClearAsync(
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress = null) =>
        hostTasks.RunAsync(
            () =>
            {
                graphics.Clear(progress: progress);
                return true;
            },
            cancellationToken);

    private HostResult<int> IsolateSelection(
        Document document,
        EntityId[] selectedIds,
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (selectedIds.Length == 0)
        {
            graphics.Clear(progress: progress);
            return new HostResult<int>.Success(0);
        }

        var inventory = new List<ObjectId>();
        var activeSpace = new HashSet<ObjectId>();
        progress?.Report(new OperationProgress(OperationStage.Reading));

        using (var transaction = document.Database.TransactionManager.StartTransaction())
        {
            foreach (var id in document.Database.GetActiveSpace().Cast<ObjectId>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                inventory.Add(id);
                activeSpace.Add(id);

                if (inventory.Count % 64 == 0)
                    progress?.Report(new OperationProgress(OperationStage.Reading, inventory.Count));
            }

            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
        }

        progress?.Report(new OperationProgress(OperationStage.Reading, inventory.Count, inventory.Count));
        var targets = new HashSet<ObjectId>();
        var hasNested = false;

        foreach (var id in selectedIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hasNested |= id.Container is not null;

            if (activeSpace.Contains(id.NativeId) && id.NativeId is {IsValid: true, IsErased: false})
                targets.Add(id.NativeId);
        }

        if (hasNested)
        {
            var count = graphics.ApplyOccurrences(
                document.Database,
                selectedIds,
                [.. inventory],
                progress,
                cancellationToken);

            return count > 0
                ? new HostResult<int>.Success(count)
                : new HostResult<int>.Unavailable("The selection contains no valid active-space occurrences.");
        }

        if (targets.Count == 0)
        {
            graphics.Clear(progress: progress);
            return new HostResult<int>.Unavailable("The selection contains no direct active-space objects.");
        }

        graphics.Apply(document.Database, [.. targets], [.. inventory], progress, cancellationToken);
        return new HostResult<int>.Success(targets.Count);
    }
}
