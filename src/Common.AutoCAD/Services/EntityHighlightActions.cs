using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace Common.AutoCAD;

/// <inheritdoc />
/// <param name="hostTasks">Queue used for native drawing access.</param>
/// <param name="graphics">Temporary graphics owned by the caller.</param>
public sealed class EntityHighlightActions(
    IHostTaskService hostTasks,
    IEntityHighlightService graphics) : IEntityHighlightActions
{
    /// <inheritdoc />
    public async Task<HostResult<int>> EmphasizeSelectionAsync(CancellationToken cancellationToken)
    {
        // Capture the current selection before the queued operation runs.
        var selectedDocument = Application.DocumentManager.MdiActiveDocument;

        if (selectedDocument is null)
            return new HostResult<int>.Unavailable("No active drawing.");

        var selection = selectedDocument.Editor.SelectImplied();

        if (selection.Status != PromptStatus.OK)
            return new HostResult<int>.Unavailable("No implied selection.");

        ObjectId[] selectedIds;

        using (var selectedSet = selection.Value)
            selectedIds = selectedSet.GetObjectIds();

        var result = await hostTasks.RunAsync(
            () => HighlightSelection(selectedIds),
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

    private HostResult<int> HighlightSelection(ObjectId[] selectedIds)
    {
        var document = Application.DocumentManager.MdiActiveDocument;

        ObjectId[] inventory;

        using (var transaction = document.Database.TransactionManager.StartTransaction())
        {
            inventory = document.Database.GetActiveSpace().Cast<ObjectId>().ToArray();
            transaction.Commit();
        }

        var targets = selectedIds.Intersect(inventory).ToArray();

        if (targets.Length == 0)
            return new HostResult<int>.Unavailable("The selection contains no direct active-space objects.");

        graphics.Apply(document.Database, targets, inventory);

        return new HostResult<int>.Success(targets.Length);
    }
}