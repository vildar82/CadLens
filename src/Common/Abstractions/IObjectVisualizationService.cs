using System.Collections.Immutable;

namespace Common;

/// <summary>Selects, isolates, or focuses placed objects in a drawing host.</summary>
public interface IObjectVisualizationService
{
    /// <summary>Replaces native selection; empty targets clear it without changing the view or isolation.</summary>
    Task<HostResult<bool>> SelectAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken);

    /// <summary>Temporarily hides other objects without changing stored model properties; an empty set clears the effect.</summary>
    Task<HostResult<bool>> IsolateAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken);

    /// <summary>Fits the active view to the available bounds of placed objects.</summary>
    Task<HostResult<bool>> FocusAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken);
}
