using System.Windows;
using CadLens.UI;
using Common;

namespace CadLens.Preview;

internal sealed class ModePreviewLens(LayersViewModel model, bool useActionStrip) : ILens
{
    private FrameworkElement? _view;

    public LensDescriptor Descriptor { get; } = new("layers", "Layers");
    public FrameworkElement View => _view ??= useActionStrip ? new LayersView(model) : new ModeLayersView(model);
    public Task ActivateAsync(CancellationToken cancellationToken) => model.ActivateAsync(cancellationToken);
    public Task<HostResult<bool>> DeactivateAsync(CancellationToken cancellationToken) => model.DeactivateAsync(cancellationToken);
    public void OnContextChanged(bool hasDrawing) => _ = model.ResetContextAsync(hasDrawing);
    public void Close(bool hostTerminating) => model.Close(hostTerminating);
}
