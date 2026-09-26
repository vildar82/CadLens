using System.Collections.Immutable;

namespace Common;

/// <summary>Applies temporary emphasis or fits the view to placed objects in a drawing host.</summary>
public interface IObjectVisualizationService
{
    /// <summary>Emphasizes objects without changing stored model properties; an empty set clears the effect.</summary>
    Task<HostResult<bool>> EmphasizeAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken);

    /// <summary>Fits the active view to the available bounds of placed objects.</summary>
    Task<HostResult<bool>> FocusAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken);
}
