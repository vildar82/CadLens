using System.Collections.Immutable;
using CadLens.Lenses;
using CadLens.UI;
using Common;
using Common.AutoCAD;

namespace CadLens.AutoCAD;

internal sealed class LayersActions(
    ILayersProvider lens,
    IEntityHighlightActions highlights,
    IEntityHighlightService graphics,
    IObjectVisualizationService visualization) : ILayersActions
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
        var result = await visualization.EmphasizeAsync(objects, cancellationToken);

        return result.Match(
            _ => objects.IsEmpty ? "Temporary effects cleared." : "Selection highlighted. Hidden objects remain hidden.",
            reason => reason);
    }

    public async Task<string> FocusAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken)
    {
        var result = await visualization.FocusAsync(objects, cancellationToken);

        return result.Match(_ => "View fitted to the available target bounds.", reason => reason);
    }
}
