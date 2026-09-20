using System.Windows;
using CadLens.Core;
using Common;

namespace CadLens.UI;

/// <summary>The Layers module owns its view, view model, and behavior.</summary>
/// <param name="viewModel">Layers-specific state and services.</param>
public sealed class LayersLens(LayersViewModel viewModel) : ILens
{
    private LayersView? _view;

    /// <inheritdoc />
    public LensDescriptor Descriptor { get; } = new("layers", "Layers");

    /// <inheritdoc />
    public FrameworkElement View => _view ??= new LayersView(viewModel);

    /// <inheritdoc />
    public Task ActivateAsync(CancellationToken cancellationToken) => viewModel.ActivateAsync(cancellationToken);

    /// <inheritdoc />
    public Task<HostResult<bool>> DeactivateAsync(CancellationToken cancellationToken) => viewModel.DeactivateAsync(cancellationToken);

    /// <inheritdoc />
    public void OnContextChanged(bool hasDrawing) => viewModel.ResetContext(hasDrawing);

    /// <inheritdoc />
    public void OnDrawingChanged() => viewModel.OnDrawingChanged();

    /// <inheritdoc />
    public void Close(bool hostTerminating) => viewModel.Close(hostTerminating);
}