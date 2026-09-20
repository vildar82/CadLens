using System.Collections.Immutable;
using CadLens.Core;
using Common;

namespace CadLens.UI;

/// <summary>Host operations for the modeless drawing explorer.</summary>
public interface IExplorerActions
{
    /// <summary>Reads the active-space inventory without changing the drawing.</summary>
    /// <param name="enabledFilters">Enabled lens option identities.</param>
    /// <param name="cancellationToken">Panel lifetime cancellation.</param>
    Task<HostResult<LensPresentation>> ReadAsync(IReadOnlySet<string> enabledFilters, CancellationToken cancellationToken);

    /// <summary>Applies the candidate rendering effect to the current implied selection.</summary>
    /// <param name="cancellationToken">Panel lifetime cancellation.</param>
    Task<string> EmphasizeAsync(CancellationToken cancellationToken);

    /// <summary>Updates emphasis for the panel selection; empty targets clear it.</summary>
    /// <param name="objects">Current group or object targets.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    Task<string> EmphasizeObjectsAsync(ImmutableArray<HostObjectId> objects, CancellationToken cancellationToken);

    /// <summary>Removes the candidate rendering effect.</summary>
    /// <param name="cancellationToken">Panel lifetime cancellation.</param>
    Task<HostResult<bool>> ClearAsync(CancellationToken cancellationToken);

    /// <summary>Fits the current selection without changing visibility.</summary>
    /// <param name="objects">Selected group or object identifiers.</param>
    /// <param name="cancellationToken">Panel lifetime cancellation.</param>
    Task<string> FocusAsync(ImmutableArray<HostObjectId> objects, CancellationToken cancellationToken);
}