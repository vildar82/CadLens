using System.Collections.Immutable;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using CadLens.Lenses;
using CadLens.UI;
using CadLens.Common;
using CadLens.Common.AutoCAD;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadLens.AutoCAD;

internal sealed class ObjectExplorerActions(
    IDrawingLensProvider lens,
    IEntityIsolationActions isolation,
    IEntityIsolationService graphics,
    IObjectVisualizationService visualization,
    IHostTaskService hostTasks) : IObjectExplorerActions
{
    public async Task<HostResult<ImmutableArray<IPlacedObjectId>>> RequestObjectsAsync(
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(OperationStage.Preparing));

        try
        {
            var document = Application.DocumentManager.MdiActiveDocument;

            if (document is null)
                return new HostResult<ImmutableArray<IPlacedObjectId>>.Unavailable(
                    "The active drawing is no longer available.");

            var space = document.Database.CurrentSpaceId;
            var selected = ReadPreselection(document, cancellationToken, progress);

            if (!selected.IsEmpty)
                return new HostResult<ImmutableArray<IPlacedObjectId>>.Success(selected);

            var result = await hostTasks.RunAsync(
                () => RequestObjects(document, space, cancellationToken, progress),
                cancellationToken);
            return result.Bind(value => value);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new HostResult<ImmutableArray<IPlacedObjectId>>.Unavailable(exception.Message);
        }
    }

    private HostResult<ImmutableArray<IPlacedObjectId>> RequestObjects(
        Document document,
        ObjectId space,
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!HasContext(document, space))
            return new HostResult<ImmutableArray<IPlacedObjectId>>.Unavailable(
                "The active drawing space has changed. Refresh to try again.");

        var selected = ReadPreselection(document, cancellationToken, progress);

        if (!selected.IsEmpty)
            return new HostResult<ImmutableArray<IPlacedObjectId>>.Success(selected);

        if (!document.Window.Focus() && !cancellationToken.IsCancellationRequested && HasContext(document, space))
            Application.MainWindow.Focus();

        cancellationToken.ThrowIfCancellationRequested();

        if (!HasContext(document, space))
            return new HostResult<ImmutableArray<IPlacedObjectId>>.Unavailable(
                "The active drawing space has changed. Refresh to try again.");

        var restoreIsolation = graphics.Suspend();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!HasContext(document, space))
                return new HostResult<ImmutableArray<IPlacedObjectId>>.Unavailable(
                    "The active drawing space has changed. Refresh to try again.");

            var options = new PromptSelectionOptions
            {
                MessageForAdding = "\n" + UiText.Current.Get("Select objects to explore:"),
                MessageForRemoval = "\n" + UiText.Current.Get("Remove objects from selection:")
            };
            progress?.Report(new OperationProgress(OperationStage.WaitingForSelection));
            cancellationToken.ThrowIfCancellationRequested();
            var result = document.Editor.GetSelection(options);
            cancellationToken.ThrowIfCancellationRequested();

            if (!HasContext(document, space))
                return new HostResult<ImmutableArray<IPlacedObjectId>>.Unavailable(
                    "The active drawing space has changed. Refresh to try again.");

            return result.Status == PromptStatus.OK
                ? new HostResult<ImmutableArray<IPlacedObjectId>>.Success(
                    ToObjects(result, cancellationToken, progress))
                : new HostResult<ImmutableArray<IPlacedObjectId>>.Unavailable(
                    "Object selection canceled. Previous view kept.");
        }
        finally
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        graphics.Clear(progress: progress);
                    }
                    catch (Exception)
                    {
                        // Failed native removals retain their owner for retry; preserve the cancellation result.
                    }
                }
                else
                {
                    restoreIsolation();
                }
            }
            finally
            {
                if (!cancellationToken.IsCancellationRequested && HasContext(document, space))
                    document.Editor.SelectObjects([]);
            }
        }
    }

    private static bool HasContext(Document document, ObjectId space) =>
        document == Application.DocumentManager.MdiActiveDocument && document.Database.CurrentSpaceId == space;

    private static ImmutableArray<IPlacedObjectId> ReadPreselection(
        Document document,
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selected = document.Editor.SelectImplied();
        cancellationToken.ThrowIfCancellationRequested();
        return selected.Status == PromptStatus.OK ? ToObjects(selected, cancellationToken, progress) : [];
    }

    private static ImmutableArray<IPlacedObjectId> ToObjects(
        PromptSelectionResult selected,
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress)
    {
        var ids = selected.Value.GetObjectIds();
        cancellationToken.ThrowIfCancellationRequested();
        var objects = new List<IPlacedObjectId>();

        for (var index = 0; index < ids.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            objects.Add(new EntityId(ids[index]));

            if ((index + 1) % 64 == 0 || index + 1 == ids.Length)
                progress?.Report(new OperationProgress(OperationStage.Selecting, index + 1, ids.Length));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return [.. objects];
    }

    public void ClearImmediately(bool hostTerminating)
    {
        try
        {
            if (!hostTerminating)
                Application.DocumentManager.MdiActiveDocument?.Editor.SelectObjects([]);
        }
        finally
        {
            graphics.Clear(!hostTerminating);
        }
    }

    public Task<HostResult<bool>> SelectAsync(
        ImmutableArray<IPlacedObjectId> objects,
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress = null) =>
        visualization.SelectAsync(objects, cancellationToken, progress);

    public async Task<HostResult<bool>> ExistsAsync(
        IPlacedObjectId target,
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var document = Application.DocumentManager.MdiActiveDocument;

        if (document is null)
            return new HostResult<bool>.Unavailable("The active drawing is no longer available.");

        if (target is not EntityId entity)
            return new HostResult<bool>.Unavailable("The targets do not belong to AutoCAD.");

        var space = document.Database.CurrentSpaceId;
        var result = await hostTasks.RunAsync(
            () => ReadExistence(document, space, entity, cancellationToken, progress),
            cancellationToken);

        return result.Bind(value => value);
    }

    private static HostResult<bool> ReadExistence(
        Document document,
        ObjectId space,
        EntityId target,
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!HasContext(document, space))
            return new HostResult<bool>.Unavailable("The active drawing space has changed. Refresh to try again.");

        progress?.Report(new OperationProgress(OperationStage.Checking, 0, 1));
        using var transaction = document.Database.TransactionManager.StartTransaction();
        var exists = target.Exists(document.Database, cancellationToken);
        progress?.Report(new OperationProgress(OperationStage.Checking, 1, 1));
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();

        return new HostResult<bool>.Success(exists);
    }

    public Task<HostResult<LensPresentation>> ReadAsync(
        DrawingGrouping grouping,
        IReadOnlyCollection<string> enabledFilters,
        ImmutableArray<IPlacedObjectId>? selectedObjects,
        CancellationToken cancellationToken,
        int? maximumObjects = null,
        IProgress<DrawingReadProgress>? progress = null) =>
        lens.LoadAsync(grouping, enabledFilters, selectedObjects, cancellationToken, maximumObjects, progress);

    public Task<HostResult<LensPresentation>> ReadBlockContentsAsync(
        IPlacedObjectId block,
        IReadOnlyCollection<string> enabledFilters,
        CancellationToken cancellationToken,
        IProgress<DrawingReadProgress>? progress = null) =>
        lens.LoadBlockContentsAsync(block, enabledFilters, cancellationToken, progress);

    public Task<HostResult<bool>> ClearIsolationAsync(
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress = null) =>
        isolation.ClearAsync(cancellationToken, progress);

    public async Task<HostResult<bool>> ClearAsync(
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress = null)
    {
        var selection = await SelectAsync([], cancellationToken, progress);
        cancellationToken.ThrowIfCancellationRequested();
        var isolated = await ClearIsolationAsync(cancellationToken, progress);

        if (selection is HostResult<bool>.Unavailable)
            return selection;

        return selection is HostResult<bool>.Success {Value: true} ? isolated : new HostResult<bool>.Success(false);
    }

    public async Task<string> IsolateObjectsAsync(
        ImmutableArray<IPlacedObjectId> objects,
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress = null)
    {
        var result = await visualization.IsolateAsync(objects, cancellationToken, progress);

        return result.Match(
            _ => objects.IsEmpty
                ? "Temporary isolation cleared."
                : "Other objects hidden temporarily. Originally hidden objects remain hidden.",
            reason => reason);
    }

    public async Task<string> FocusAsync(
        ImmutableArray<IPlacedObjectId> objects,
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress = null)
    {
        var result = await visualization.FocusAsync(objects, cancellationToken, progress);

        return result.Match(_ => "View fitted.", reason => reason);
    }
}