namespace CadLens.Common.AutoCAD;

/// <summary>Queues temporary visual isolation of drawing objects.</summary>
public interface IEntityIsolationActions
{
    /// <summary>Keeps direct or nested targets visible; an empty set clears the effect.</summary>
    /// <param name="objects">Occurrence targets captured by the caller.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    Task<HostResult<int>> IsolateAsync(EntityId[] objects, CancellationToken cancellationToken);

    /// <summary>Clears temporary isolation in host context.</summary>
    /// <param name="cancellationToken">Request cancellation.</param>
    Task<HostResult<bool>> ClearAsync(CancellationToken cancellationToken);
}
