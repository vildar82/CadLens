using Autodesk.AutoCAD.Colors;

namespace Common.AutoCAD;

/// <summary>Colors for temporary entity emphasis.</summary>
/// <param name="Accent">Color applied to targets.</param>
/// <param name="Dimmed">Color applied to other inventory entities.</param>
public sealed record EntityHighlightOptions(EntityColor Accent, EntityColor Dimmed);