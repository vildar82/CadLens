using System.Windows;
using CadLens.Core;
using Common;

namespace CadLens.UI;

/// <summary>A self-contained lens with its own view, state, and services.</summary>
/// <remarks>Close must cancel outstanding work and release owned effects without waiting for another host idle event.</remarks>
public interface ILens
{
    /// <summary>Identity and title available without loading drawing data.</summary>
    LensDescriptor Descriptor { get; }

    /// <summary>The lens-owned WPF content, accessed only on the UI thread while active.</summary>
    FrameworkElement View { get; }

    /// <summary>Starts this lens's behavior without imposing a data or command model.</summary>
    /// <param name="cancellationToken">Active-lens lifetime, canceled before deactivation or close.</param>
    Task ActivateAsync(CancellationToken cancellationToken);

    /// <summary>Settles lens-owned work and removes effects before another lens can activate.</summary>
    /// <param name="cancellationToken">Cleanup lifetime, independent of the canceled active-lens token.</param>
    Task<HostResult<bool>> DeactivateAsync(CancellationToken cancellationToken);

    /// <summary>Invalidates lens state when the drawing or space changes, without activating the lens.</summary>
    /// <param name="hasDrawing">Whether a drawing is available in the new context.</param>
    void OnContextChanged(bool hasDrawing);

    /// <summary>Cancels work and releases owned effects synchronously when the panel closes.</summary>
    /// <param name="hostTerminating">Whether shutdown forbids redraw or access to surviving document views.</param>
    void Close(bool hostTerminating);

    /// <summary>Notifies the active lens of drawing edits; the lens decides how to react.</summary>
    void OnDrawingChanged();
}