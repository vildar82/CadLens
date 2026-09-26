namespace Common.AutoCAD;

/// <summary>Queues temporary highlighting of drawing objects.</summary>
public interface IEntityHighlightActions
{
    /// <summary>Highlights direct active-space objects; an empty set clears the effect.</summary>
    /// <param name="objects">Native targets captured by the caller.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    Task<HostResult<int>> EmphasizeAsync(Autodesk.AutoCAD.DatabaseServices.ObjectId[] objects, CancellationToken cancellationToken);

    /// <summary>Clears temporary colors in host context.</summary>
    /// <param name="cancellationToken">Request cancellation.</param>
    Task<HostResult<bool>> ClearAsync(CancellationToken cancellationToken);
}
