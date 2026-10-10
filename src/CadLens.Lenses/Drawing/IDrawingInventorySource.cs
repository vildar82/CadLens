using System.Collections.Immutable;
using CadLens.Common;

namespace CadLens.Lenses;

/// <summary>Reads the current space in the host's execution context.</summary>
public interface IDrawingInventorySource
{
    /// <summary>Reads the current layer and entity data.</summary>
    /// <param name="selectedObjects">Captured selection, or null for all direct current-space objects.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <param name="maximumObjects">Maximum direct-space objects for automatic loading; null allows an explicit full read. Selected scope ignores the limit.</param>
    Task<HostResult<DrawingInventory>> ReadAsync(
        ImmutableArray<IPlacedObjectId>? selectedObjects,
        CancellationToken cancellationToken,
        int? maximumObjects = null);
}