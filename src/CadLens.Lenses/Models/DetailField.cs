namespace CadLens.Lenses;

/// <summary>A lens-provided label and value for the generic detail panel.</summary>
/// <param name="Label">Field label.</param>
/// <param name="Value">Formatted field value.</param>
public sealed record DetailField(string Label, string Value);