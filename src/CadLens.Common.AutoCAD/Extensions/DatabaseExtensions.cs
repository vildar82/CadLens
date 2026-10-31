using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

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
            var displayed = new VisibleBounds(cancellationToken);
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

                if (displayed.Read(entity) is not { } extents)
                    continue;

                var combined = bounds ?? extents;
                combined.AddExtents(extents);
                bounds = combined;
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new OperationProgress(OperationStage.Reading, completed, total));
            return bounds;
        }

        /// <summary>Limits bounds to the drawing extents, which contain everything Zoom Extents displays.</summary>
        /// <remarks>Custom objects such as Civil 3D surfaces can report far more than they draw.</remarks>
        /// <param name="bounds">Combined target bounds.</param>
        /// <param name="modelSpace">Whether the targets belong to model space.</param>
        /// <returns>The clipped bounds, or the original ones when the extents are unset or elsewhere.</returns>
        internal Extents3d ClipToExtents(Extents3d bounds, bool modelSpace)
        {
            var low = modelSpace ? database.Extmin : database.Pextmin;
            var high = modelSpace ? database.Extmax : database.Pextmax;
            var min = new Point3d(
                Math.Max(bounds.MinPoint.X, low.X),
                Math.Max(bounds.MinPoint.Y, low.Y),
                Math.Max(bounds.MinPoint.Z, low.Z));
            var max = new Point3d(
                Math.Min(bounds.MaxPoint.X, high.X),
                Math.Min(bounds.MaxPoint.Y, high.Y),
                Math.Min(bounds.MaxPoint.Z, high.Z));

            return min.X <= max.X && min.Y <= max.Y && min.Z <= max.Z ? new Extents3d(min, max) : bounds;
        }
    }

    extension(Extents3d bounds)
    {
        /// <summary>Returns the axis-aligned box around all eight transformed corners.</summary>
        /// <remarks>Rotation moves the extreme corners, so the minimum and maximum alone are not enough.</remarks>
        /// <param name="transform">Transformation into the target coordinate system.</param>
        internal Extents3d TransformCorners(Matrix3d transform)
        {
            var min = bounds.MinPoint;
            var max = bounds.MaxPoint;
            List<Point3d> corners = [];

            foreach (var x in new[] {min.X, max.X})
            {
                foreach (var y in new[] {min.Y, max.Y})
                {
                    foreach (var z in new[] {min.Z, max.Z})
                        corners.Add(new Point3d(x, y, z).TransformBy(transform));
                }
            }

            return new Extents3d(
                new Point3d(corners.Min(p => p.X), corners.Min(p => p.Y), corners.Min(p => p.Z)),
                new Point3d(corners.Max(p => p.X), corners.Max(p => p.Y), corners.Max(p => p.Z)));
        }
    }
}
