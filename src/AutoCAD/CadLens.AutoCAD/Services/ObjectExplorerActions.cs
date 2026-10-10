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
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var document = Application.DocumentManager.MdiActiveDocument;

            if (document is null)
                return new HostResult<ImmutableArray<IPlacedObjectId>>.Unavailable(
                    "The active drawing is no longer available.");

            var space = document.Database.CurrentSpaceId;
            var selected = ReadPreselection(document);

            if (!selected.IsEmpty)
                return new HostResult<ImmutableArray<IPlacedObjectId>>.Success(selected);

            var result = await hostTasks.RunAsync(
                () => RequestObjects(document, space, cancellationToken),
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
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!HasContext(document, space))
            return new HostResult<ImmutableArray<IPlacedObjectId>>.Unavailable(
                "The active drawing space has changed. Refresh to try again.");

        var selected = ReadPreselection(document);

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
            var result = document.Editor.GetSelection(options);
            cancellationToken.ThrowIfCancellationRequested();

            if (!HasContext(document, space))
                return new HostResult<ImmutableArray<IPlacedObjectId>>.Unavailable(
                    "The active drawing space has changed. Refresh to try again.");

            return result.Status == PromptStatus.OK
                ? new HostResult<ImmutableArray<IPlacedObjectId>>.Success(ToObjects(result))
                : new HostResult<ImmutableArray<IPlacedObjectId>>.Unavailable(
                    "Object selection canceled. Previous view kept.");
        }
        finally
        {
            try
            {
                restoreIsolation();
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

    private static ImmutableArray<IPlacedObjectId> ReadPreselection(Document document)
    {
        var selected = document.Editor.SelectImplied();
        return selected.Status == PromptStatus.OK ? ToObjects(selected) : [];
    }

    private static ImmutableArray<IPlacedObjectId> ToObjects(PromptSelectionResult selected) =>
        [.. selected.Value.GetObjectIds().Select<ObjectId, IPlacedObjectId>(id => new EntityId(id))];

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
        CancellationToken cancellationToken) =>
        visualization.SelectAsync(objects, cancellationToken);

    public Task<HostResult<LensPresentation>> ReadAsync(
        DrawingGrouping grouping,
        IReadOnlyCollection<string> enabledFilters,
        ImmutableArray<IPlacedObjectId>? selectedObjects,
        CancellationToken cancellationToken,
        int? maximumObjects = null) =>
        lens.LoadAsync(grouping, enabledFilters, selectedObjects, cancellationToken, maximumObjects);

    public Task<HostResult<bool>> ClearIsolationAsync(CancellationToken cancellationToken) =>
        isolation.ClearAsync(cancellationToken);

    public async Task<HostResult<bool>> ClearAsync(CancellationToken cancellationToken)
    {
        var selection = await SelectAsync([], cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var isolated = await ClearIsolationAsync(cancellationToken);

        if (selection is HostResult<bool>.Unavailable)
            return selection;

        return selection is HostResult<bool>.Success {Value: true} ? isolated : new HostResult<bool>.Success(false);
    }

    public async Task<string> IsolateObjectsAsync(
        ImmutableArray<IPlacedObjectId> objects,
        CancellationToken cancellationToken)
    {
        var result = await visualization.IsolateAsync(objects, cancellationToken);

        return result.Match(
            _ => objects.IsEmpty
                ? "Temporary isolation cleared."
                : "Other objects hidden temporarily. Originally hidden objects remain hidden.",
            reason => reason);
    }

    public async Task<string> FocusAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken)
    {
        var result = await visualization.FocusAsync(objects, cancellationToken);

        return result.Match(_ => "View fitted.", reason => reason);
    }
}