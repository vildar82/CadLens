using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

namespace Common.AutoCAD;

/// <summary>Reads current-space records and object bounds in an active transaction.</summary>
public static class DatabaseExtensions
{
    /// <summary>Opens the database's current space in the active transaction.</summary>
    /// <param name="database">Database with an active regular transaction.</param>
    public static BlockTableRecord GetActiveSpace(this Database database) =>
        database.CurrentSpaceId.GetObject<BlockTableRecord>()!;

    /// <summary>Combines usable extents of direct objects in the current space. Requires an active regular transaction.</summary>
    /// <param name="database">Database with an active regular transaction.</param>
    /// <param name="objects">Candidate objects; invalid, erased, or out-of-space objects are skipped.</param>
    public static Extents3d? ReadBounds(this Database database, IEnumerable<ObjectId> objects)
    {
        Extents3d? bounds = null;

        foreach (var target in objects)
        {
            if (!target.IsValid || target.Database != database)
                continue;

            var entity = target.GetObject<Entity>();

            if (entity is null || entity.OwnerId != database.CurrentSpaceId)
                continue;

            try
            {
                var extents = entity.GeometricExtents;

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

        return bounds;
    }

    private static bool HasUsableBounds(Extents3d bounds) =>
        double.IsFinite(bounds.MinPoint.X) && double.IsFinite(bounds.MinPoint.Y) && double.IsFinite(bounds.MinPoint.Z) &&
        double.IsFinite(bounds.MaxPoint.X) && double.IsFinite(bounds.MaxPoint.Y) && double.IsFinite(bounds.MaxPoint.Z) &&
        bounds.MinPoint.X <= bounds.MaxPoint.X && bounds.MinPoint.Y <= bounds.MaxPoint.Y && bounds.MinPoint.Z <= bounds.MaxPoint.Z;
}
