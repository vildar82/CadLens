using CadLens.Lenses;

namespace CadLens.UI;

/// <summary>An available primitive property and its grouping selection.</summary>
/// <param name="Id">Property used in the composite group key.</param>
/// <param name="Label">App-owned property label.</param>
/// <param name="IsSelected">Whether this property participates in grouping.</param>
public sealed record GroupingOption(DrawingPropertyId Id, string Label, bool IsSelected);
