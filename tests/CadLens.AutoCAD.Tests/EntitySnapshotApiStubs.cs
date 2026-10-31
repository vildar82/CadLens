using System.Collections;

// ReSharper disable MemberCanBeMadeStatic.Global, MemberCanBeMadeStatic.Local -- Native instance APIs.

// These doubles let the production snapshot reader run without native AutoCAD binaries.
namespace Autodesk.AutoCAD.Runtime
{
    internal enum ErrorStatus
    {
        NotApplicable, NotImplementedYet, InvalidInput, DegenerateGeometry, NullExtents, InvalidExtents,
        GeneralModelingFailure
    }

    internal sealed class Exception(ErrorStatus errorStatus) : System.Exception
    {
        internal ErrorStatus ErrorStatus { get; } = errorStatus;
    }

    internal sealed record RXClass(string Name);
}

namespace Autodesk.AutoCAD.Colors
{
    internal readonly struct EntityColor
    {
        internal static int LookUpRgb(byte colorIndex) => colorIndex == 1
            ? unchecked((int) 0xC3FF0000)
            : throw new NotSupportedException("Only the tested palette entry is available in this API double.");
    }

    internal enum ColorMethod { ByLayer,
        ByAci
    }

    internal sealed class Color : IDisposable
    {
        internal bool IsByLayer { get; init; } = true;
        internal bool IsByBlock { get; init; }
        internal bool IsByAci { get; init; }
        internal bool IsByColor { get; init; }
        internal bool HasBookName { get; init; }
        internal short ColorIndex { get; init; }
        internal byte Red { get; init; }
        internal byte Green { get; init; }
        internal byte Blue { get; init; }
        internal string ColorName { get; init; } = "";
        internal string BookName { get; init; } = "";
        internal ColorMethod ColorMethod { get; init; } = ColorMethod.ByLayer;

        internal static Color FromColorIndex(ColorMethod method, short index) =>
            new()
            {
                IsByLayer = method == ColorMethod.ByLayer, IsByAci = method == ColorMethod.ByAci, ColorIndex = index
            };

        public void Dispose() { }
    }

    internal readonly struct Transparency(byte alpha = byte.MaxValue)
    {
        public Transparency() : this(byte.MaxValue) { }
        internal bool IsInvalid { get; init; }
        internal bool IsByLayer { get; init; } = true;
        internal bool IsByBlock { get; init; }
        internal byte Alpha => alpha;
    }
}

namespace Autodesk.AutoCAD.DatabaseServices
{
    internal enum LineWeight { ByLayer, ByBlock, ByLineWeightDefault }

    public partial class Entity
    {
        internal Colors.Color Color { get; set; } = new();
        internal LineWeight LineWeight { get; set; } = LineWeight.ByLayer;
        internal string Linetype { get; set; } = "ByLayer";
        internal double LinetypeScale { get; set; } = 1;
        internal Colors.Transparency Transparency { get; set; } = new();
        internal Runtime.RXClass GetRXClass() => new("AcDb" + GetType().Name);
        internal Func<Geometry.Matrix3d, Entity>? TransformedCopy { get; init; }
        internal Entity? LastTransformedCopy { get; private set; }
        internal Geometry.Matrix3d AppliedTransform { get; private set; } = Geometry.Matrix3d.Identity;

        internal Entity GetTransformedCopy(Geometry.Matrix3d transform)
        {
            var copy = TransformedCopy?.Invoke(transform) ?? (Entity) Clone();
            copy.TransformBy(transform);
            LastTransformedCopy = copy;

            return copy;
        }

        internal object Clone()
        {
            var copy = (Entity) MemberwiseClone();
            copy.ObjectId = default;

            return copy;
        }

        internal void TransformBy(Geometry.Matrix3d transform) => AppliedTransform = transform * AppliedTransform;
    }

    internal class Curve : Entity
    {
        internal Func<double> ReadArea { get; init; } = () => 0;
        internal double Area => ReadArea();
        internal double Length { get; init; }
        internal bool Closed { get; init; }
        internal double StartParam { get; init; }
        internal double EndParam { get; init; }
        internal double GetDistanceAtParameter(double parameter) => parameter * Length;
    }

    internal sealed class Ellipse : Curve;

    internal sealed class Polyline : Curve
    {
        internal int NumberOfVertices { get; init; }
        internal double ConstantWidth { get; init; }
        internal bool HasWidth { get; init; }
        internal double Thickness { get; init; }
    }

    internal sealed class Polyline2d(params ObjectId[] vertices) : Curve, IEnumerable
    {
        internal double ConstantWidth { get; init; }
        internal double Thickness { get; init; }
        public IEnumerator GetEnumerator() => vertices.GetEnumerator();
    }

    internal sealed class Polyline3d(params ObjectId[] vertices) : Curve, IEnumerable
    {
        public IEnumerator GetEnumerator() => vertices.GetEnumerator();
    }

    internal sealed class Vertex2d : Entity;
    internal sealed class PolylineVertex3d : Entity;

    internal class BlockReference : Entity
    {
        internal bool IsDynamicBlock { get; init; }
        internal ObjectId BlockTableRecord { get; init; }
        internal ObjectId DynamicBlockTableRecord { get; init; }
        internal Geometry.Matrix3d BlockTransform { get; init; } = Geometry.Matrix3d.Identity;
        internal Runtime.ErrorStatus? AttributesError { get; init; }
        internal List<ObjectId> AttributeCollection
        {
            get => AttributesError is { } error ? throw new Runtime.Exception(error) : field;
        } = [];
        internal Runtime.ErrorStatus? DynamicPropertiesError { get; init; }
        internal DynamicBlockReferencePropertyCollection DynamicBlockReferencePropertyCollection
        {
            get => DynamicPropertiesError is { } error ? throw new Runtime.Exception(error) : field;
            init;
        } = new();
    }

    internal sealed class MInsertBlock : BlockReference;

    internal sealed class AttributeDefinition : DBText
    {
        internal bool Invisible { get; init; }
        internal bool Constant { get; init; }
    }

    internal enum DynamicBlockReferencePropertyUnitsType { NoUnits, Angular, Distance, Area }

    internal sealed class DynamicBlockReferencePropertyCollection : IEnumerable, IDisposable
    {
        internal List<DynamicBlockReferenceProperty> Properties { get; } = [];
        internal Runtime.ErrorStatus? EnumerationError { get; init; }
        internal bool IsDisposed { get; private set; }

        public IEnumerator GetEnumerator() => EnumerationError is { } error
            ? throw new Runtime.Exception(error)
            : Properties.GetEnumerator();

        public void Dispose() => IsDisposed = true;
    }

    internal sealed class DynamicBlockReferenceProperty
    {
        internal Runtime.ErrorStatus? NameError { get; init; }
        internal string PropertyName
        {
            get => NameError is { } error ? throw new Runtime.Exception(error) : field;
            init;
        } = "";
        internal Runtime.ErrorStatus? ValueError { get; init; }
        internal Action? ValueReading { get; init; }
        internal int ValueReadCount { get; private set; }
        internal object? Value
        {
            get
            {
                ValueReadCount++;
                ValueReading?.Invoke();

                return ValueError is { } error ? throw new Runtime.Exception(error) : field;
            }
            set;
        }
        internal Runtime.ErrorStatus? UnitsError { get; init; }
        internal DynamicBlockReferencePropertyUnitsType UnitsType
        {
            get => UnitsError is { } error ? throw new Runtime.Exception(error) : field;
            init;
        }
        internal bool ReadOnly { get; init; }
    }

    internal enum HatchPatternType { PreDefined }

    internal sealed class Hatch : Entity
    {
        internal Func<double> ReadArea { get; init; } = () => 0;
        internal double Area => ReadArea();
        internal int NumberOfLoops { get; init; }
        internal bool IsGradient { get; init; }
        internal bool IsSolidFill { get; init; }
        internal string GradientName { get; init; } = "";
        internal string PatternName { get; init; } = "";
        internal HatchPatternType PatternType { get; init; }
        internal double PatternAngle { get; init; }
        internal double PatternScale { get; init; }
    }

    internal sealed class Spline : Curve
    {
        internal int NumControlPoints { get; init; }
        internal int NumFitPoints { get; init; }
    }

    internal sealed class Mline : Entity
    {
        internal int NumberOfVertices { get; init; }
    }

    internal sealed class Line : Curve
    {
        internal double Thickness { get; init; }
    }

    internal sealed class Circle : Curve
    {
        internal double Radius { get; init; }
        internal double Circumference => 2 * Math.PI * Radius;
        internal double Thickness { get; init; }
    }

    internal sealed class Arc : Curve
    {
        internal double Radius { get; init; }
        internal double StartAngle { get; init; }
        internal double EndAngle { get; init; }
        internal double Thickness { get; init; }
    }

    internal class DBText : Entity
    {
        internal double Height { get; init; }
        internal Runtime.ErrorStatus? TextError { get; init; }
        internal string TextString
        {
            get => TextError is { } error ? throw new Runtime.Exception(error) : field;
            set;
        } = "";
        internal string TextStyleName { get; init; } = "";
    }

    internal sealed class AttributeReference : DBText
    {
        internal bool Invisible { get; init; }
        internal Runtime.ErrorStatus? TagError { get; init; }
        internal string Tag
        {
            get => TagError is { } error ? throw new Runtime.Exception(error) : field;
            init;
        } = "";
        internal bool IsMTextAttribute { get; init; }
        internal MText MTextAttribute { get; init; } = new();
    }

    internal sealed class MText : Entity
    {
        internal double TextHeight { get; init; }
        internal string Text { get; init; } = "";
        internal string TextStyleName { get; init; } = "";
    }
}

namespace Autodesk.AutoCAD.Geometry
{
    internal readonly record struct Point3d(double X, double Y, double Z)
    {
        internal static Point3d Origin => default;

        internal Point3d TransformBy(Matrix3d matrix)
        {
            var cosine = Math.Cos(matrix.Angle);
            var sine = Math.Sin(matrix.Angle);

            return new Point3d(
                (X * cosine - Y * sine) * matrix.Scale + matrix.X,
                (X * sine + Y * cosine) * matrix.Scale + matrix.Y,
                Z * matrix.Scale + matrix.Z);
        }
    }

    internal readonly record struct Vector3d(double X, double Y, double Z);

    internal readonly record struct Matrix3d(double Scale, double X = 0, double Y = 0, double Z = 0, double Angle = 0)
    {
        internal Matrix3d(double[] values) : this(values[0], values[3], values[7], values[11]) { }

        internal double[] ToArray() => [Scale, 0, 0, X, 0, Scale, 0, Y, 0, 0, Scale, Z, 0, 0, 0, 1];

        internal static Matrix3d Identity => new(1);

        internal static Matrix3d Scaling(double scale, Point3d center) =>
            new(scale, center.X * (1 - scale), center.Y * (1 - scale), center.Z * (1 - scale));

        internal static Matrix3d Displacement(Vector3d vector) => new(1, vector.X, vector.Y, vector.Z);

        internal static Matrix3d RotationAboutZ(double angle) => new(1, Angle: angle);

        public static Matrix3d operator *(Matrix3d left, Matrix3d right) => new(
            left.Scale * right.Scale,
            left.Scale * right.X + left.X,
            left.Scale * right.Y + left.Y,
            left.Scale * right.Z + left.Z,
            left.Angle + right.Angle);
    }
}
