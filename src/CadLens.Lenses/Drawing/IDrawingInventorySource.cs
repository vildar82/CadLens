using System.Collections.Immutable;
using CadLens.Common;

namespace CadLens.Lenses;

/// <summary>Reads the current space in the host's execution context.</summary>
public interface IDrawingInventorySource
{
    /// <summary>Reads the current layer and entity data.</summary>
    /// <param name="selectedObjects">Captured selection, or null for all direct current-space objects.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <param name="maximumObjects">
    /// Automatic traversal limit; null allows a full read. Selected scope ignores this limit.
    /// </param>
    /// <param name="includeBlockContents">Whether local blocks are replaced by their recursive placed contents.</param>
    Task<HostResult<DrawingInventory>> ReadAsync(
        ImmutableArray<IPlacedObjectId>? selectedObjects,
        CancellationToken cancellationToken,
        int? maximumObjects = null,
        bool includeBlockContents = false);
}