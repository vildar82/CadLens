namespace CadLens.Lenses;

/// <summary>Known drawing properties shared by grouping, metrics, and details.</summary>
public enum DrawingPropertyId
{
    /// <summary>Assigned layer.</summary>
    Layer,
    /// <summary>Assigned color.</summary>
    Color,
    /// <summary>Assigned linetype.</summary>
    Linetype,
    /// <summary>Assigned lineweight.</summary>
    Lineweight,
    /// <summary>Entity linetype scale.</summary>
    LinetypeScale,
    /// <summary>Assigned transparency.</summary>
    Transparency,
    /// <summary>Stored vertices.</summary>
    Vertices,
    /// <summary>Closed state.</summary>
    Closed,
    /// <summary>Length in drawing units.</summary>
    Length,
    /// <summary>Constant polyline width.</summary>
    Width,
    /// <summary>Extrusion thickness.</summary>
    Thickness,
    /// <summary>Live direct definition entities.</summary>
    DefinitionEntities,
    /// <summary>Readable block definition name.</summary>
    BlockName,
    /// <summary>Insertion attributes.</summary>
    Attributes,
    /// <summary>Dynamic insertion state.</summary>
    Dynamic,
    /// <summary>External reference state.</summary>
    ExternalReference,
    /// <summary>Hatch boundary loops.</summary>
    BoundaryLoops,
    /// <summary>Solid, pattern, or gradient fill.</summary>
    FillKind,
    /// <summary>Hatch pattern name.</summary>
    Pattern,
    /// <summary>Hatch pattern angle.</summary>
    PatternAngle,
    /// <summary>Hatch pattern scale.</summary>
    PatternScale,
    /// <summary>Native hatch pattern category.</summary>
    PatternType,
    /// <summary>Hatch gradient name.</summary>
    Gradient,
    /// <summary>Spline control points.</summary>
    ControlPoints,
    /// <summary>Spline fit points.</summary>
    FitPoints,
    /// <summary>Circle or arc radius.</summary>
    Radius,
    /// <summary>Arc start angle.</summary>
    StartAngle,
    /// <summary>Arc end angle.</summary>
    EndAngle,
    /// <summary>Text height.</summary>
    TextHeight,
    /// <summary>Plain text contents.</summary>
    Text,
    /// <summary>Assigned text style name.</summary>
    TextStyle
}