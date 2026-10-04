using System.Windows;
using CadLens.Lenses;
using CadLens.Common;

namespace CadLens.UI;

/// <summary>Shared exploration behavior with independent state for each drawing grouping.</summary>
public sealed class ObjectExplorerLens : ILens, IDisposable
{
    private ObjectExplorerView? _view;

    /// <summary>Creates a lens with its own navigation, options, and host action state.</summary>
    /// <param name="actions">Context-checked host operations.</param>
    /// <param name="grouping">Root organization for this lens.</param>
    /// <param name="settings">Optional persistent preferences for this lens's grouping.</param>
    public ObjectExplorerLens(
        IObjectExplorerActions actions,
        DrawingGrouping grouping,
        SettingsService? settings = null)
    {
        ViewModel = new ObjectExplorerViewModel(actions, grouping, settings);
        Descriptor = grouping == DrawingGrouping.Layers
            ? new LensDescriptor("layers", "Layers", "Browse active-space objects by layer, then by type and object.")
            : new LensDescriptor("object-types", "Objects", "Browse active-space objects by type across layers, then by object.");
    }

    /// <summary>State owned by this lens instance.</summary>
    public ObjectExplorerViewModel ViewModel { get; }

    /// <inheritdoc />
    public LensDescriptor Descriptor { get; }

    /// <inheritdoc />
    public FrameworkElement View => _view ??= new ObjectExplorerView(ViewModel);

    /// <inheritdoc />
    public Task ActivateAsync(CancellationToken cancellationToken) => ViewModel.ActivateAsync(cancellationToken);

    /// <inheritdoc />
    public Task<HostResult<bool>> DeactivateAsync(CancellationToken cancellationToken) =>
        ViewModel.DeactivateAsync(cancellationToken);

    /// <inheritdoc />
    public void OnContextChanged(bool hasDrawing) => _ = ViewModel.ResetContextAsync(hasDrawing);

    /// <inheritdoc />
    public void Close(bool hostTerminating) => ViewModel.Close(hostTerminating);

    /// <inheritdoc />
    public void Dispose() => ViewModel.Dispose();
}