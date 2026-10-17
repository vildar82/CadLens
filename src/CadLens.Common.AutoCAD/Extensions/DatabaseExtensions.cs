using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

namespace CadLens.Common.AutoCAD;

/// <summary>Reads current-space records and object bounds in an active transaction.</summary>
public static class DatabaseExtensions
{
    extension(Database database)
    {
        /// <summary>Opens the database's current space in the active transaction.</summary>
        public BlockTableRecord GetActiveSpace() =>
            database.CurrentSpaceId.GetObject<BlockTableRecord>()!;

        /// <summary>Combines usable extents of direct objects in the current space. Requires an active regular transaction.</summary>
        /// <param name="objects">Candidate objects; invalid, erased, or out-of-space objects are skipped.</param>
        /// <param name="cancellationToken">Cooperative request cancellation.</param>
        /// <param name="progress">Optional bounds-reading progress.</param>
        public Extents3d? ReadBounds(
            IEnumerable<ObjectId> objects,
            IProgress<OperationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Extents3d? bounds = null;
            var completed = 0;
            var total = objects is IReadOnlyCollection<ObjectId> collection ? collection.Count : 0;

            foreach (var target in objects)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (++completed % 64 == 0)
                    progress?.Report(new OperationProgress(OperationStage.Reading, completed, total));
                if (!target.IsValid || target.Database != database)
                    continue;

                var entity = target.GetObject<Entity>();

                if (entity is null || entity.OwnerId != database.CurrentSpaceId)
                    continue;

                try
                {
                    var extents = entity.GeometricExtents;
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!HasUsableBounds(extents))
                        continue;

                    var combined = bounds ?? extents;
                    combined.AddExtents(extents);
                    bounds = combined;
                }
                catch (Exception exception) when (
                    exception.ErrorStatus is ErrorStatus.NullExtents or ErrorStatus.InvalidExtents or ErrorStatus.NotApplicable)
                {
                    // Bounds are optional for custom or empty entities; other native failures reach the queue.
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new OperationProgress(OperationStage.Reading, completed, total));
            return bounds;
        }
    }

    private static bool HasUsableBounds(Extents3d bounds) =>
        bounds.MinPoint.X.IsFinite() && bounds.MinPoint.Y.IsFinite() && bounds.MinPoint.Z.IsFinite() &&
        bounds.MaxPoint.X.IsFinite() && bounds.MaxPoint.Y.IsFinite() && bounds.MaxPoint.Z.IsFinite() &&
        bounds.MinPoint.X <= bounds.MaxPoint.X && bounds.MinPoint.Y <= bounds.MaxPoint.Y && bounds.MinPoint.Z <= bounds.MaxPoint.Z;
}
