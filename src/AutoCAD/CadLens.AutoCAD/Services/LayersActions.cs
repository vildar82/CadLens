using CadLens.UI;
using CadLens.Lenses;
using System.Collections.Immutable;
using CadLens.Core;
using Common;
using Common.AutoCAD;

namespace CadLens.AutoCAD;

internal sealed class LayersActions(
    ILayersProvider lens,
    IEntityHighlightActions highlights,
    IEntityHighlightService graphics,
    IHostActions host) : ILayersActions
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

    public async Task<string> EmphasizeObjectsAsync(ImmutableArray<HostObjectId> objects, CancellationToken cancellationToken)
    {
        var result = await host.EmphasizeAsync(objects, cancellationToken);

        return result.Match(
            _ => objects.IsEmpty ? "Temporary effects cleared." : "Selection highlighted. Hidden objects remain hidden.",
            reason => reason);
    }

    public async Task<string> FocusAsync(ImmutableArray<HostObjectId> objects, CancellationToken cancellationToken)
    {
        var result = await host.FocusAsync(objects, cancellationToken);

        return result.Match(_ => "View fitted to the available target bounds.", reason => reason);
    }
}