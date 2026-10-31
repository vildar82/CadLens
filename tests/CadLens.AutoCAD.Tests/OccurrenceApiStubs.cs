using System.Collections;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

// Test doubles expose occurrence transforms and transient ownership without native rendering.
namespace Autodesk.AutoCAD.Geometry
{
    internal sealed class IntegerCollection;
}

namespace Autodesk.AutoCAD.DatabaseServices
{
    internal sealed class DBDictionary(params string[] keys) : DBObject
    {
        internal bool Contains(string key) => keys.Contains(key);
    }

    internal sealed class DBObjectCollection : IDisposable, IEnumerable
    {
        internal List<DBObject> Items { get; } = [];
        internal int Count => Items.Count;
        public IEnumerator GetEnumerator() => Items.GetEnumerator();

        public void Dispose()
        {
        }
    }

    public partial class Entity
    {
        internal Extents3d GeometricExtents { get; init; } = new(new Point3d(0, 0, 0), new Point3d(1, 1, 0));
        internal Action<DBObjectCollection>? OnExplode { get; init; }
        internal void Explode(DBObjectCollection parts) => OnExplode!(parts);
    }

    /// <summary>Test double for world-space geometric bounds.</summary>
    public struct Extents3d
    {
        internal Extents3d(Point3d min, Point3d max)
        {
            MinPoint = min;
            MaxPoint = max;
        }

        internal Point3d MinPoint { get; private set; }
        internal Point3d MaxPoint { get; private set; }

        internal void AddExtents(Extents3d other)
        {
            MinPoint = new Point3d(
                Math.Min(MinPoint.X, other.MinPoint.X),
                Math.Min(MinPoint.Y, other.MinPoint.Y),
                Math.Min(MinPoint.Z, other.MinPoint.Z));
            MaxPoint = new Point3d(
                Math.Max(MaxPoint.X, other.MaxPoint.X),
                Math.Max(MaxPoint.Y, other.MaxPoint.Y),
                Math.Max(MaxPoint.Z, other.MaxPoint.Z));
        }
    }

    public sealed partial class Database
    {
        private static readonly Point3d UnsetMin = new(1e20, 1e20, 1e20);
        private static readonly Point3d UnsetMax = new(-1e20, -1e20, -1e20);
        internal Point3d Extmin { get; set; } = UnsetMin;
        internal Point3d Extmax { get; set; } = UnsetMax;
        internal Point3d Pextmin { get; set; } = UnsetMin;
        internal Point3d Pextmax { get; set; } = UnsetMax;
    }

    internal sealed class DBPoint : Entity;
    internal sealed class Solid : Entity;
    internal sealed class Face : Entity;
    internal class Dimension : Entity;
    internal sealed class AlignedDimension : Dimension;

    internal sealed class ViewTableRecord : IDisposable
    {
        internal bool PerspectiveEnabled { get; init; }

        public void Dispose()
        {
        }
    }
}

namespace Autodesk.AutoCAD.ApplicationServices
{
    internal sealed partial class Editor
    {
        internal bool PerspectiveEnabled { get; init; }
        internal Extents3d? ZoomBounds { get; set; }
        internal ViewTableRecord GetCurrentView() => new() {PerspectiveEnabled = PerspectiveEnabled};
    }
}

namespace CadLens.Common.AutoCAD
{
    internal static class OccurrenceExtensions
    {
        internal static void Zoom(
            this Autodesk.AutoCAD.ApplicationServices.Editor editor,
            Extents3d bounds,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            editor.ZoomBounds = bounds;
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}