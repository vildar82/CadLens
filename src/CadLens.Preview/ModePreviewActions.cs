using System.Collections.Immutable;
using CadLens.Lenses;
using CadLens.UI;
using Common;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CadLens.Preview;

internal sealed class ModePreviewActions(PreviewLayersActions actions) : ObservableObject, ILayersActions
{
    public ImmutableArray<IPlacedObjectId> CameraTargets { get; private set; } = [];
    public ImmutableArray<IPlacedObjectId> SelectedTargets { get; private set; } = [];
    public ImmutableArray<IPlacedObjectId> IsolatedTargets { get; private set; } = [];

    public string Evidence => $"Camera: {Describe(CameraTargets)}\nSelection: {Describe(SelectedTargets)}\nIsolation: {Describe(IsolatedTargets)}";

    public async Task<HostResult<bool>> SelectAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken)
    {
        var result = await actions.SelectAsync(objects, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (result is HostResult<bool>.Success)
            SelectedTargets = objects;

        OnPropertyChanged(nameof(Evidence));
        return result;
    }

    public void ClearImmediately(bool hostTerminating)
    {
        actions.ClearImmediately(hostTerminating);
        SelectedTargets = [];
        IsolatedTargets = [];
        OnPropertyChanged(nameof(Evidence));
    }

    public Task<HostResult<LayersPresentation>> ReadAsync(IReadOnlySet<string> enabledFilters, CancellationToken cancellationToken) =>
        actions.ReadAsync(enabledFilters, cancellationToken);

    public async Task<string> IsolateObjectsAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken)
    {
        await actions.IsolateObjectsAsync(objects, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        IsolatedTargets = objects;
        OnPropertyChanged(nameof(Evidence));
        return $"Preview: isolated {objects.Length:N0} objects.";
    }

    public async Task<HostResult<bool>> ClearAsync(CancellationToken cancellationToken)
    {
        var result = await actions.ClearAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (result is HostResult<bool>.Success)
        {
            SelectedTargets = [];
            IsolatedTargets = [];
        }

        OnPropertyChanged(nameof(Evidence));
        return result;
    }

    public async Task<HostResult<bool>> ClearIsolationAsync(CancellationToken cancellationToken)
    {
        var result = await actions.ClearIsolationAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (result is HostResult<bool>.Success)
            IsolatedTargets = [];

        OnPropertyChanged(nameof(Evidence));
        return result;
    }

    public async Task<string> FocusAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken)
    {
        await actions.FocusAsync(objects, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        CameraTargets = objects;
        OnPropertyChanged(nameof(Evidence));
        return $"Preview: camera fitted to {objects.Length:N0} objects.";
    }

    private static string Describe(ImmutableArray<IPlacedObjectId> objects) =>
        objects.IsEmpty ? "none" : $"{objects.Length:N0} objects (first #{objects[0].DisplayId})";
}
