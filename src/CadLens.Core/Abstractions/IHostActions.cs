using System.Collections.Immutable;
using Common;

namespace CadLens.Core;

/// <summary>Performs explicit drawing host operations.</summary>
public interface IHostActions
{
    /// <summary>Changes temporary visualization; an empty target set clears it.</summary>
    /// <param name="objects">Objects to emphasize.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    Task<HostResult<bool>> EmphasizeAsync(
        ImmutableArray<IPlacedObjectId> objects,
        CancellationToken cancellationToken);

    /// <summary>Fits usable bounds without revealing hidden objects.</summary>
    /// <param name="objects">Focus targets.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    Task<HostResult<bool>> FocusAsync(
        ImmutableArray<IPlacedObjectId> objects,
        CancellationToken cancellationToken);
}
