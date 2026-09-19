using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using CadLens.Core;
using CadLens.UI;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadLens.AutoCAD;

internal sealed class VerificationActions(
    IHostTaskService hostTasks,
    ILensProvider lens,
    IVerificationGraphics graphics) : IVerificationActions
{
    public Task<HostResult<LensPresentation>> ReadAsync(CancellationToken cancellationToken) =>
        lens.LoadAsync(new HashSet<string>(), cancellationToken);

    public async Task<string> EmphasizeAsync(CancellationToken cancellationToken)
    {
        // Capture the current selection before the queued operation runs.
        var selectedDocument = Application.DocumentManager.MdiActiveDocument;

        if (selectedDocument is null)
            return "No active drawing.";

        var selection = selectedDocument.Editor.SelectImplied();

        if (selection.Status != PromptStatus.OK)
            return "Nothing selected. Select objects in the drawing, then click Highlight selection.";

        ObjectId[] selectedIds;

        using (var selectedSet = selection.Value)
            selectedIds = selectedSet.GetObjectIds();

        var result = await hostTasks.RunAsync(
            () => HighlightSelection(selectedIds),
            cancellationToken);

        return result.Match(value => value, reason => reason);
    }

    public async Task<string> ClearAsync(CancellationToken cancellationToken)
    {
        var result = await hostTasks.RunAsync(ClearHighlight, cancellationToken);

        return result.Match(value => value, reason => reason);
    }

    private string HighlightSelection(ObjectId[] selectedIds)
    {
        var document = Application.DocumentManager.MdiActiveDocument;

        using var transaction = document.Database.TransactionManager.StartTransaction();

        var inventory = document.Database.GetActiveSpace().GetObjects<Entity>()
            .Select(entity => entity.ObjectId)
            .ToArray();
        var targets = selectedIds.Intersect(inventory).ToArray();

        transaction.Commit();

        if (targets.Length == 0)
            return "The selection contains no direct active-space objects.";

        graphics.Apply(document.Database, targets, inventory);

        return $"Highlight requested for {targets.Length} objects. " +
               "Use Clear highlight to restore normal appearance.";
    }

    private string ClearHighlight()
    {
        graphics.Clear();

        return "Temporary effects cleared.";
    }
}