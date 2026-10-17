using Autodesk.AutoCAD.DatabaseServices;

namespace CadLens.Common.AutoCAD;

/// <summary>Owns temporary visual isolation. The caller clears it before leaving the drawing context.</summary>
public interface IEntityIsolationService
{
    /// <summary>Suppresses non-target inventory graphics in the active document.</summary>
    /// <param name="database">Active document database.</param>
    /// <param name="targets">Entities to keep visible.</param>
    /// <param name="inventory">Entities affected by the effect.</param>
    /// <param name="cancellationToken">Cooperative request cancellation.</param>
    /// <param name="progress">Optional operation stage and item counts.</param>
    void Apply(
        Database database,
        ObjectId[] targets,
        ObjectId[] inventory,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Shows nested targets with temporary graphics and returns the number of valid occurrences.</summary>
    /// <param name="database">Active document database.</param>
    /// <param name="targets">Direct objects or occurrence paths captured by the caller.</param>
    /// <param name="inventory">Direct active-space entities affected by the effect.</param>
    /// <param name="cancellationToken">Cooperative request cancellation.</param>
    /// <param name="progress">Optional operation stage and item counts.</param>
    int ApplyOccurrences(
        Database database,
        EntityId[] targets,
        ObjectId[] inventory,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Temporarily removes isolation and returns an action that restores it if its drawing context is still valid.</summary>
    Action Suspend();

    /// <summary>Removes the effect.</summary>
    /// <param name="redraw">Whether to regenerate the affected active document.</param>
    /// <param name="progress">Optional cleanup stage and item counts.</param>
    void Clear(bool redraw = true, IProgress<OperationProgress>? progress = null);
}
