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

    /// <summary>Removes the candidate rendering effect.</summary>
    /// <param name="cancellationToken">Panel lifetime cancellation.</param>
    Task<string> ClearAsync(CancellationToken cancellationToken);
}