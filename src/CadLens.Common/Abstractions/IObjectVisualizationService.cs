using System.Collections.Immutable;

namespace CadLens.Common;

/// <summary>Selects, isolates, paints, or focuses placed objects in a drawing host.</summary>
public interface IObjectVisualizationService
{
    /// <summary>Replaces native selection; empty targets clear it without changing the view or isolation.</summary>
    Task<HostResult<bool>> SelectAsync(
        ImmutableArray<IPlacedObjectId> objects,
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress = null);

    /// <summary>Temporarily hides other objects without changing stored model properties; an empty set clears the effect.</summary>
    Task<HostResult<bool>> IsolateAsync(
        ImmutableArray<IPlacedObjectId> objects,
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress = null);

    /// <summary>Temporarily redraws objects in the given colors without changing stored model properties; empty groups clear the effect.</summary>
    Task<HostResult<bool>> PaintAsync(
        ImmutableArray<PaintGroup> groups,
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress = null);

    /// <summary>Fits the active view to the available bounds of placed objects.</summary>
    Task<HostResult<bool>> FocusAsync(
        ImmutableArray<IPlacedObjectId> objects,
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress = null);
}
