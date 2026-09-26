namespace Common.AutoCAD;

/// <summary>Queues temporary visual isolation of drawing objects.</summary>
public interface IEntityIsolationActions
{
    /// <summary>Keeps direct active-space targets visible; an empty set clears the effect.</summary>
    /// <param name="objects">Native targets captured by the caller.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    Task<HostResult<int>> IsolateAsync(Autodesk.AutoCAD.DatabaseServices.ObjectId[] objects, CancellationToken cancellationToken);

    /// <summary>Clears temporary isolation in host context.</summary>
    /// <param name="cancellationToken">Request cancellation.</param>
    Task<HostResult<bool>> ClearAsync(CancellationToken cancellationToken);
}
