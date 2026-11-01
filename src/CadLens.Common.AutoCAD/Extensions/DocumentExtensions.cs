using Autodesk.AutoCAD.ApplicationServices;

namespace CadLens.Common.AutoCAD;

/// <summary>Native document operations shared by temporary graphics.</summary>
internal static class DocumentExtensions
{
    /// <summary>Regenerates every viewport so temporary graphics take effect immediately.</summary>
    internal static void RegenerateAllViewports(
        this Document document,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(OperationStage.Regenerating));
        const int allViewports = 1;
        dynamic drawing = document.GetAcadDocument();
        cancellationToken.ThrowIfCancellationRequested();
        drawing.Regen(allViewports);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(OperationStage.Regenerating, 1, 1));
    }
}