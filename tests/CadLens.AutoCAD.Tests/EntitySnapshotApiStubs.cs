using System.Collections;

// ReSharper disable MemberCanBeMadeStatic.Global -- Native API doubles must preserve instance member signatures.

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
    internal enum ColorMethod { ByLayer }

    internal sealed class Color : IDisposable
    {
        internal bool IsByLayer => true;
        internal bool IsByBlock => false;
        internal bool IsByAci => false;
        internal bool IsByColor => false;
        internal bool HasBookName => false;
        internal short ColorIndex => 0;
        internal byte Red => 0;
        internal byte Green => 0;
        internal byte Blue => 0;
        internal string ColorName => "";
        internal string BookName => "";
        internal ColorMethod ColorMethod => ColorMethod.ByLayer;
        public void Dispose() { }
    }

    internal readonly struct Transparency
    {
        internal bool IsInvalid => false;
        internal bool IsByLayer => true;
        internal bool IsByBlock => false;
        internal byte Alpha => 255;
    }
}

namespace Autodesk.AutoCAD.DatabaseServices
{
    internal enum LineWeight { ByLayer, ByBlock, ByLineWeightDefault }

    public partial class Entity
    {
        internal Colors.Color Color => new();
        internal LineWeight LineWeight => LineWeight.ByLayer;
        internal string Linetype => "ByLayer";
        internal double LinetypeScale => 1;
        internal Colors.Transparency Transparency => new();
        internal Runtime.RXClass GetRXClass() => new("AcDb" + GetType().Name);
    }

    internal class Curve : Entity
    {
        internal Func<double> ReadArea { get; init; } = () => 0;
        internal double Area => ReadArea();
        internal double Length { get; init; }
        internal bool Closed { get; init; }
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

    internal sealed class BlockReference : Entity
    {
        internal bool IsDynamicBlock { get; init; }
        internal ObjectId BlockTableRecord { get; init; }
        internal ObjectId DynamicBlockTableRecord { get; init; }
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
        internal double EndParam { get; init; }
        internal double GetDistanceAtParameter(double parameter) => parameter * Length;
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
        internal Runtime.ErrorStatus? TagError { get; init; }
        internal string Tag
        {
            get => TagError is { } error ? throw new Runtime.Exception(error) : field;
            init;
        } = "";
        internal bool IsMTextAttribute { get; init; }
        internal MText MTextAttribute { get; init; } = new();
    }

    internal sealed class MText : Entity, IDisposable
    {
        internal bool IsDisposed { get; private set; }
        internal double TextHeight { get; init; }
        internal string Text { get; init; } = "";
        internal string TextStyleName { get; init; } = "";
        public void Dispose() => IsDisposed = true;
    }
}
