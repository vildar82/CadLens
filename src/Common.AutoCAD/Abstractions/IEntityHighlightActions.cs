namespace Common.AutoCAD;

/// <summary>Queues temporary highlighting of the current implied selection.</summary>
public interface IEntityHighlightActions
{
    /// <summary>Captures selection immediately, then highlights direct active-space entities in host context.</summary>
    /// <param name="cancellationToken">Request cancellation.</param>
    Task<HostResult<int>> EmphasizeSelectionAsync(CancellationToken cancellationToken);

    /// <summary>Clears temporary colors in host context.</summary>
    /// <param name="cancellationToken">Request cancellation.</param>
    Task<HostResult<bool>> ClearAsync(CancellationToken cancellationToken);
}