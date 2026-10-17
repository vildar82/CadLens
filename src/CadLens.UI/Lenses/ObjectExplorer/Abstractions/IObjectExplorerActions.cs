using CadLens.Lenses;
using System.Collections.Immutable;
using CadLens.Common;

namespace CadLens.UI;

/// <summary>Host operations for the modeless drawing explorer.</summary>
public interface IObjectExplorerActions
{
    /// <summary>Returns CAD preselection, or asks the user to select objects when none are selected.</summary>
    /// <param name="cancellationToken">Panel lifetime cancellation.</param>
    /// <param name="progress">Optional operation step and item counts.</param>
    Task<HostResult<ImmutableArray<IPlacedObjectId>>> RequestObjectsAsync(
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress = null);

    /// <summary>
    /// Checks only one placed object and its container path without reloading the inventory.
    /// </summary>
    /// <param name="target">Collected object identity to verify in the active space.</param>
    /// <param name="cancellationToken">Panel lifetime cancellation.</param>
    /// <param name="progress">Optional operation step and item counts.</param>
    Task<HostResult<bool>> ExistsAsync(
        IPlacedObjectId target,
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress = null);

    /// <summary>Clears selection and graphics synchronously at context and lifetime boundaries.</summary>
    /// <param name="hostTerminating">Whether shutdown forbids editor access and regeneration.</param>
    void ClearImmediately(bool hostTerminating);

    /// <summary>Replaces native selection; empty targets clear it.</summary>
    /// <param name="objects">Collected drawing targets.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <param name="progress">Optional operation step and item counts.</param>
    Task<HostResult<bool>> SelectAsync(
        ImmutableArray<IPlacedObjectId> objects,
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress = null);

    /// <summary>Clears only temporary visual isolation.</summary>
    /// <param name="cancellationToken">Cancellation before cleanup starts.</param>
    /// <param name="progress">Optional operation step and item counts.</param>
    Task<HostResult<bool>> ClearIsolationAsync(
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress = null);

    /// <summary>Reads the active-space inventory without changing the drawing.</summary>
    /// <param name="grouping">Root organization for this lens.</param>
    /// <param name="enabledFilters">Enabled lens option identities.</param>
    /// <param name="selectedObjects">Captured selection, or null for all direct current-space objects.</param>
    /// <param name="cancellationToken">Panel lifetime cancellation.</param>
    /// <param name="maximumObjects">Maximum direct-space objects for automatic loading; null allows an explicit full read. Selected scope ignores the limit.</param>
    /// <param name="progress">Optional progress through root objects and their placed contents.</param>
    Task<HostResult<LensPresentation>> ReadAsync(
        DrawingGrouping grouping,
        IReadOnlyCollection<string> enabledFilters,
        ImmutableArray<IPlacedObjectId>? selectedObjects,
        CancellationToken cancellationToken,
        int? maximumObjects = null,
        IProgress<DrawingReadProgress>? progress = null);

    /// <summary>
    /// Reads the recursive placed contents of one local block insertion without changing the drawing.
    /// </summary>
    /// <param name="block">Top-level insertion to explore.</param>
    /// <param name="enabledFilters">Enabled visibility inclusion options.</param>
    /// <param name="cancellationToken">Panel lifetime cancellation.</param>
    /// <param name="progress">Optional progress through root objects and their placed contents.</param>
    Task<HostResult<LensPresentation>> ReadBlockContentsAsync(
        IPlacedObjectId block,
        IReadOnlyCollection<string> enabledFilters,
        CancellationToken cancellationToken,
        IProgress<DrawingReadProgress>? progress = null);

    /// <summary>Isolates the panel selection; empty targets clear it.</summary>
    /// <param name="objects">Current group or object targets.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <param name="progress">Optional operation step and item counts.</param>
    Task<string> IsolateObjectsAsync(
        ImmutableArray<IPlacedObjectId> objects,
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress = null);

    /// <summary>Clears CAD selection and temporary rendering.</summary>
    /// <param name="cancellationToken">Panel lifetime cancellation.</param>
    /// <param name="progress">Optional operation step and item counts.</param>
    Task<HostResult<bool>> ClearAsync(
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress = null);

    /// <summary>Fits the current selection without changing visibility.</summary>
    /// <param name="objects">Selected group or object identifiers.</param>
    /// <param name="cancellationToken">Panel lifetime cancellation.</param>
    /// <param name="progress">Optional operation step and item counts.</param>
    Task<string> FocusAsync(
        ImmutableArray<IPlacedObjectId> objects,
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress = null);
}
