namespace CadLens.Lenses;

/// <summary>Provides display names for entity runtime types.</summary>
public static class EntityTypeExtensions
{
    /// <summary>Returns a familiar label, keeping unknown custom type names unchanged.</summary>
    /// <param name="key">Host runtime type name.</param>
    public static string GetTypeLabel(this string key) => key switch
    {
        "AcDbLine" => "Line",
        "AcDbPolyline" => "Polyline",
        "AcDb2dPolyline" => "2D polyline",
        "AcDb3dPolyline" => "3D polyline",
        "AcDbArc" => "Arc",
        "AcDbCircle" => "Circle",
        "AcDbBlockReference" => "Block insertion",
        "AcDbMInsertBlock" => "Block array",
        "AcDbMText" => "MText",
        "AcDbText" => "Text",
        "AcDbHatch" => "Hatch",
        "AcDbSpline" => "Spline",
        "AcDbMline" => "Multiline",
        "AcDbEllipse" => "Ellipse",
        "AcDbPoint" => "Point",
        "AcDbSolid" => "2D solid",
        "AcDbFace" => "3D face",
        "AcDbTrace" => "Trace",
        "AcDbRegion" => "Region",
        "AcDb3dSolid" => "3D solid",
        "AcDbXline" => "Construction line",
        "AcDbRay" => "Ray",
        "AcDbHelix" => "Helix",
        "AcDbLeader" => "Leader",
        "AcDbMLeader" => "Multileader",
        "AcDbTable" => "Table",
        "AcDbViewport" => "Viewport",
        "AcDbRasterImage" => "Raster image",
        "AcDbWipeout" => "Wipeout",
        "AcDbAttributeDefinition" => "Attribute definition",
        "AcDbAlignedDimension" => "Aligned dimension",
        "AcDbRotatedDimension" => "Linear dimension",
        "AcDbArcDimension" => "Arc dimension",
        "AcDbRadialDimension" => "Radial dimension",
        "AcDbDiametricDimension" => "Diameter dimension",
        "AcDb2LineAngularDimension" => "Angular dimension",
        "AcDb3PointAngularDimension" => "Angular dimension",
        "AcDbOrdinateDimension" => "Ordinate dimension",
        _ => key
    };
}