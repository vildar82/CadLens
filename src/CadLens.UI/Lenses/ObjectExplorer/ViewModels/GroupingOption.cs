using CadLens.Lenses;

namespace CadLens.UI;

/// <summary>An available primitive property and its grouping selection.</summary>
/// <param name="Id">Property used in the composite group key.</param>
/// <param name="IsSelected">Whether this property participates in grouping.</param>
public sealed record GroupingOption(DrawingPropertyKey Id, bool IsSelected)
{
    /// <summary>Localized property name with the drawing-owned name preserved.</summary>
    public string Label => DrawingValueFormatter.FormatPropertyLabel(Id);
}
