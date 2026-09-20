using CadLens.Core;
using CadLens.UI;
using Common;
using Common.AutoCAD;

namespace CadLens.AutoCAD;

internal sealed class ExplorerActions(ILensProvider lens, IEntityHighlightActions highlights) : IExplorerActions
{
    public Task<HostResult<LensPresentation>> ReadAsync(IReadOnlySet<string> enabledFilters, CancellationToken cancellationToken) =>
        lens.LoadAsync(enabledFilters, cancellationToken);

    public async Task<string> EmphasizeAsync(CancellationToken cancellationToken)
    {
        var result = await highlights.EmphasizeSelectionAsync(cancellationToken);

        return result.Match(
            count => $"Highlight requested for {count} objects. Use Clear highlight to restore normal appearance.",
            reason => reason);
    }

    public async Task<string> ClearAsync(CancellationToken cancellationToken)
    {
        var result = await highlights.ClearAsync(cancellationToken);

        return result.Match(_ => "Temporary effects cleared.", reason => reason);
    }
}