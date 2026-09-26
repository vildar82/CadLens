using CadLens.Lenses;
using System.Collections.Immutable;
using Common;

namespace CadLens.UI;

/// <summary>Host operations for the modeless drawing explorer.</summary>
public interface ILayersActions
{
    /// <summary>Clears selection and graphics synchronously at context and lifetime boundaries.</summary>
    /// <param name="hostTerminating">Whether shutdown forbids editor access and regeneration.</param>
    void ClearImmediately(bool hostTerminating);

    /// <summary>Replaces native selection; empty targets clear it.</summary>
    Task<HostResult<bool>> SelectAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken);

    /// <summary>Clears only temporary visual isolation.</summary>
    Task<HostResult<bool>> ClearIsolationAsync(CancellationToken cancellationToken);

    /// <summary>Reads the active-space inventory without changing the drawing.</summary>
    /// <param name="enabledFilters">Enabled lens option identities.</param>
    /// <param name="cancellationToken">Panel lifetime cancellation.</param>
    Task<HostResult<LayersPresentation>> ReadAsync(IReadOnlySet<string> enabledFilters, CancellationToken cancellationToken);

    /// <summary>Isolates the panel selection; empty targets clear it.</summary>
    /// <param name="objects">Current group or object targets.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    Task<string> IsolateObjectsAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken);

    /// <summary>Clears CAD selection and temporary rendering.</summary>
    /// <param name="cancellationToken">Panel lifetime cancellation.</param>
    Task<HostResult<bool>> ClearAsync(CancellationToken cancellationToken);

    /// <summary>Fits the current selection without changing visibility.</summary>
    /// <param name="objects">Selected group or object identifiers.</param>
    /// <param name="cancellationToken">Panel lifetime cancellation.</param>
    Task<string> FocusAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken);
}
