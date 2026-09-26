using System.Collections.Immutable;
using CadLens.Lenses;
using CadLens.UI;
using Common;
using Common.AutoCAD;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadLens.AutoCAD;

internal sealed class LayersActions(
    ILayersProvider lens,
    IEntityHighlightActions highlights,
    IEntityHighlightService graphics,
    IObjectVisualizationService visualization) : ILayersActions
{
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

    public Task<HostResult<bool>> SelectAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken) =>
        visualization.SelectAsync(objects, cancellationToken);

    public Task<HostResult<LayersPresentation>> ReadAsync(IReadOnlySet<string> enabledFilters, CancellationToken cancellationToken) =>
        lens.LoadAsync(enabledFilters, cancellationToken);

    public Task<HostResult<bool>> ClearHighlightAsync(CancellationToken cancellationToken) =>
        highlights.ClearAsync(cancellationToken);

    public async Task<HostResult<bool>> ClearAsync(CancellationToken cancellationToken)
    {
        var selection = await SelectAsync([], cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var highlight = await ClearHighlightAsync(cancellationToken);

        if (selection is HostResult<bool>.Unavailable)
            return selection;

        return selection is HostResult<bool>.Success { Value: true } ? highlight : new HostResult<bool>.Success(false);
    }

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

        return result.Match(_ => "View fitted.", reason => reason);
    }
}
