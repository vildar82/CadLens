using CadLens.Core;
using Common;

namespace CadLens.UI;

/// <summary>Host operations for the early modeless integration verification panel.</summary>
public interface IVerificationActions
{
    /// <summary>Reads the active-space inventory without changing the drawing.</summary>
    /// <param name="cancellationToken">Panel lifetime cancellation.</param>
    Task<HostResult<LensPresentation>> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Applies the candidate rendering effect to the current implied selection.</summary>
    /// <param name="cancellationToken">Panel lifetime cancellation.</param>
    Task<string> EmphasizeAsync(CancellationToken cancellationToken);

    /// <summary>Removes the candidate rendering effect.</summary>
    /// <param name="cancellationToken">Panel lifetime cancellation.</param>
    Task<string> ClearAsync(CancellationToken cancellationToken);
}