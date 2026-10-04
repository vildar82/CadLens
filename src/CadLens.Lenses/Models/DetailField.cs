namespace CadLens.Lenses;

/// <summary>A lens-provided label and value for the generic detail panel.</summary>
/// <param name="Label">Field label.</param>
/// <param name="Value">Formatted field value.</param>
/// <param name="ValueKind">Explicit formatting and localization behavior.</param>
/// <param name="TypedValue">Detached value for typed formatting.</param>
/// <param name="IsLabelRaw">Preserve a drawing-owned label instead of translating it.</param>
/// <param name="PropertyKey">Property identity for a localized label that preserves drawing-owned names.</param>
public sealed record DetailField(
    string Label,
    string Value,
    DetailValueKind ValueKind = DetailValueKind.RawText,
    DrawingValue? TypedValue = null,
    bool IsLabelRaw = false,
    DrawingPropertyKey? PropertyKey = null);

/// <summary>How to display a detail without guessing from its label.</summary>
public enum DetailValueKind
{
    /// <summary>Preserve drawing-provided text.</summary>
    RawText,
    /// <summary>Translate an application-owned term.</summary>
    ApplicationText,
    /// <summary>Translate a known primitive name and preserve custom names.</summary>
    PrimitiveType,
    /// <summary>Translate the composed layer visibility explanation.</summary>
    LayerVisibility,
    /// <summary>Format the typed value, including unavailable values.</summary>
    TypedValue
}