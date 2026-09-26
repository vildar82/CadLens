using System.Collections.Immutable;
using CadLens.Lenses;
using CadLens.UI;
using Common;
using Common.AutoCAD;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadLens.AutoCAD;

internal sealed class LayersActions(
    ILayersProvider lens,
    IEntityIsolationActions isolation,
    IEntityIsolationService graphics,
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

    public Task<HostResult<bool>> ClearIsolationAsync(CancellationToken cancellationToken) =>
        isolation.ClearAsync(cancellationToken);

    public async Task<HostResult<bool>> ClearAsync(CancellationToken cancellationToken)
    {
        var selection = await SelectAsync([], cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var isolated = await ClearIsolationAsync(cancellationToken);

        if (selection is HostResult<bool>.Unavailable)
            return selection;

        return selection is HostResult<bool>.Success { Value: true } ? isolated : new HostResult<bool>.Success(false);
    }

    public async Task<string> IsolateObjectsAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken)
    {
        var result = await visualization.IsolateAsync(objects, cancellationToken);

        return result.Match(
            _ => objects.IsEmpty ? "Temporary isolation cleared." : "Other objects hidden temporarily. Originally hidden objects remain hidden.",
            reason => reason);
    }

    public async Task<string> FocusAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken)
    {
        var result = await visualization.FocusAsync(objects, cancellationToken);

        return result.Match(_ => "View fitted.", reason => reason);
    }
}
