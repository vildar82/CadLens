using Common;

namespace CadLens.Lenses;

/// <summary>Reads the current space in the host's execution context.</summary>
public interface IDrawingInventorySource
{
    /// <summary>Reads the current layer and entity data.</summary>
    /// <param name="cancellationToken">Request cancellation.</param>
    Task<HostResult<DrawingInventory>> ReadAsync(CancellationToken cancellationToken);
}