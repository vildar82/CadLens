using System.Collections.Immutable;
using CadLens.Common;

namespace CadLens.Lenses;

/// <summary>Builds the drawing explorer inventory for the chosen grouping.</summary>
public interface IDrawingLensProvider
{
    /// <summary>Builds the presentation for the current drawing and options.</summary>
    /// <param name="grouping">Root grouping for this request.</param>
    /// <param name="enabledFilters">Enabled filter identities.</param>
    /// <param name="selectedObjects">Captured selection, or null for all direct current-space objects.</param>
    /// <param name="cancellationToken">Cancellation of managed work.</param>
    /// <param name="maximumObjects">Maximum direct-space objects for automatic loading; null allows an explicit full read. Selected scope ignores the limit.</param>
    Task<HostResult<LensPresentation>> LoadAsync(
        DrawingGrouping grouping,
        IReadOnlyCollection<string> enabledFilters,
        ImmutableArray<IPlacedObjectId>? selectedObjects,
        CancellationToken cancellationToken,
        int? maximumObjects = null);
}