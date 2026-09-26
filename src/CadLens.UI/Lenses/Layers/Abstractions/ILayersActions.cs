using CadLens.Lenses;
using System.Collections.Immutable;
using Common;

namespace CadLens.UI;

/// <summary>Host operations for the modeless drawing explorer.</summary>
public interface ILayersActions
{
    /// <summary>Detaches owned graphics synchronously at context and lifetime boundaries.</summary>
    /// <param name="redraw">Whether the current host view can be regenerated.</param>
    void ClearImmediately(bool redraw);

    /// <summary>Reads the active-space inventory without changing the drawing.</summary>
    /// <param name="enabledFilters">Enabled lens option identities.</param>
    /// <param name="cancellationToken">Panel lifetime cancellation.</param>
    Task<HostResult<LayersPresentation>> ReadAsync(IReadOnlySet<string> enabledFilters, CancellationToken cancellationToken);

    /// <summary>Updates emphasis for the panel selection; empty targets clear it.</summary>
    /// <param name="objects">Current group or object targets.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    Task<string> EmphasizeObjectsAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken);

    /// <summary>Removes the candidate rendering effect.</summary>
    /// <param name="cancellationToken">Panel lifetime cancellation.</param>
    Task<HostResult<bool>> ClearAsync(CancellationToken cancellationToken);

    /// <summary>Fits the current selection without changing visibility.</summary>
    /// <param name="objects">Selected group or object identifiers.</param>
    /// <param name="cancellationToken">Panel lifetime cancellation.</param>
    Task<string> FocusAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken);
}
