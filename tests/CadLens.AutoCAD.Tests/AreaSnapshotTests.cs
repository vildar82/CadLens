using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using CadLens.Lenses;
using Xunit;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

namespace CadLens.AutoCAD;

/// <summary>Native area extraction remains separate from primary metrics and unavailable geometry.</summary>
public sealed class AreaSnapshotTests
{
    /// <summary>Every curve uses its native area, including open curves and unlisted runtime types.</summary>
    [Fact]
    public void CurvesExposeNativeAreaWithoutChangingPrimaryMetrics()
    {
        (Curve Curve, DrawingPropertyId? Metric)[] cases =
        [
            (new Polyline {ReadArea = () => 12.3451}, DrawingPropertyId.Vertices),
            (new Polyline2d {ReadArea = () => 12.3451}, DrawingPropertyId.Vertices),
            (new Polyline3d {ReadArea = () => 12.3451}, DrawingPropertyId.Vertices),
            (new Line {ReadArea = () => 12.3451, Length = 17}, DrawingPropertyId.Length),
            (new Circle {ReadArea = () => 12.3451, Radius = 3}, DrawingPropertyId.Radius),
            (new Arc {ReadArea = () => 12.3451}, DrawingPropertyId.Radius),
            (new Spline {ReadArea = () => 12.3451}, DrawingPropertyId.ControlPoints),
            (new Ellipse {ReadArea = () => 12.3451}, null),
            (new CustomCurve {ReadArea = () => 12.3451}, null)
        ];

        foreach (var (curve, metric) in cases)
        {
            Assert.False(curve.Closed);
            var snapshot = Read(curve);
            Assert.Equal(new DrawingNumberValue(12.3451, DrawingUnit.Area), snapshot.Properties![DrawingPropertyId.Area]);
            Assert.Equal(metric, snapshot.PrimaryMetric);
        }

        Assert.Equal(new DrawingNumberValue(17, DrawingUnit.Distance), Read(cases[3].Curve).Properties![DrawingPropertyId.Length]);
        Assert.Equal(new DrawingNumberValue(3, DrawingUnit.Distance), Read(cases[4].Curve).Properties![DrawingPropertyId.Radius]);
    }

    /// <summary>Pattern, solid, and gradient hatches keep their native net area and boundary-loop metric.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void HatchesExposeAreaIndependentlyOfFillKind(bool solid, bool gradient)
    {
        var snapshot = Read(new Hatch
        {
            ReadArea = () => 42.125,
            IsSolidFill = solid,
            IsGradient = gradient,
            NumberOfLoops = 2
        });

        Assert.Equal(new DrawingNumberValue(42.125, DrawingUnit.Area), snapshot.Properties![DrawingPropertyId.Area]);
        Assert.Equal(DrawingPropertyId.BoundaryLoops, snapshot.PrimaryMetric);
        Assert.Equal(new DrawingNumberValue(2, DrawingUnit.Count), snapshot.Properties[DrawingPropertyId.BoundaryLoops]);
    }

    /// <summary>Unsupported, nonplanar, and failed native calculations remain observed but unavailable.</summary>
    [Fact]
    public void NativeAreaFailuresDoNotRemoveTheEntityOrInventZero()
    {
#if NETFRAMEWORK
        var statuses = Enum.GetValues(typeof(ErrorStatus)).Cast<ErrorStatus>();
#else
        var statuses = Enum.GetValues<ErrorStatus>();
#endif
        foreach (var status in statuses)
        {
            Func<double> unavailable = () => throw new Exception(status);
            var curve = Read(new Line {ReadArea = unavailable, Length = 17});
            var hatch = Read(new Hatch {ReadArea = unavailable, NumberOfLoops = 2});

            Assert.Null(curve.Properties![DrawingPropertyId.Area]);
            Assert.Null(hatch.Properties![DrawingPropertyId.Area]);
            Assert.Equal(new DrawingNumberValue(17, DrawingUnit.Distance), curve.Properties[DrawingPropertyId.Length]);
            Assert.Equal(new DrawingNumberValue(2, DrawingUnit.Count), hatch.Properties[DrawingPropertyId.BoundaryLoops]);
            Assert.Contains(DrawingPropertyId.Area, DrawingProperties.GetAvailableFields([curve, hatch]));
        }
    }

    /// <summary>Invalid native numbers are unavailable, while a valid native zero stays distinguishable.</summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0)]
    public void NativeAreasKeepFiniteValuesOnly(double area)
    {
        var expected = area == 0 ? new DrawingNumberValue(0, DrawingUnit.Area) : null;

        Assert.Equal(expected, Read(new Ellipse {ReadArea = () => area}).Properties![DrawingPropertyId.Area]);
        Assert.Equal(expected, Read(new Hatch {ReadArea = () => area}).Properties![DrawingPropertyId.Area]);
    }

    /// <summary>Area handling does not swallow managed cancellation or add area to non-curve entities.</summary>
    [Fact]
    public void AreaHandlingPreservesCancellationAndNonCurveProperties()
    {
        Assert.Throws<OperationCanceledException>(() => Read(new CustomCurve
        {
            ReadArea = () => throw new OperationCanceledException()
        }));
        Assert.DoesNotContain(DrawingPropertyId.Area, Read(new DBText()).Properties!.Keys);
    }

    private static EntitySnapshot Read(Entity entity)
    {
        new Database().Add(entity);

        return new AutoCadEntitySnapshotReader(CancellationToken.None).Read(entity);
    }

    private sealed class CustomCurve : Curve;
}
