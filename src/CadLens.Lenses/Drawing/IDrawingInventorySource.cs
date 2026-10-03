using System.Collections.Immutable;
using Common;

namespace CadLens.Lenses;

/// <summary>Reads the current space in the host's execution context.</summary>
public interface IDrawingInventorySource
{
    /// <summary>Reads the current layer and entity data.</summary>
    /// <param name="selectedObjects">Captured selection, or null for all direct current-space objects.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    Task<HostResult<DrawingInventory>> ReadAsync(
        ImmutableArray<IPlacedObjectId>? selectedObjects,
        CancellationToken cancellationToken);
}