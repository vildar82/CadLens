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
    /// <param name="maximumObjects">
    /// Automatic traversal limit; null allows a full read. Selected scope ignores this limit.
    /// </param>
    /// <param name="progress">Optional progress through root objects and their placed contents.</param>
    Task<HostResult<LensPresentation>> LoadAsync(
        DrawingGrouping grouping,
        IReadOnlyCollection<string> enabledFilters,
        ImmutableArray<IPlacedObjectId>? selectedObjects,
        CancellationToken cancellationToken,
        int? maximumObjects = null,
        IProgress<DrawingReadProgress>? progress = null);

    /// <summary>
    /// Builds an Object Types presentation for the recursive placed contents of one local block insertion.
    /// </summary>
    /// <param name="block">Top-level insertion to explore.</param>
    /// <param name="enabledFilters">Enabled visibility inclusion options.</param>
    /// <param name="cancellationToken">Cancellation of managed work.</param>
    /// <param name="progress">Optional progress through root objects and their placed contents.</param>
    Task<HostResult<LensPresentation>> LoadBlockContentsAsync(
        IPlacedObjectId block,
        IReadOnlyCollection<string> enabledFilters,
        CancellationToken cancellationToken,
        IProgress<DrawingReadProgress>? progress = null);
}