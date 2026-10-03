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
        "AcDbMText" => "MText",
        "AcDbText" => "Text",
        "AcDbHatch" => "Hatch",
        _ => key
    };
}