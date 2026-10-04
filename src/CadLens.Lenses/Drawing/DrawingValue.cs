using CadLens.Common;

namespace CadLens.Lenses;

/// <summary>A typed, host-independent property value shared by details and grouping.</summary>
public abstract record DrawingValue;

/// <summary>A raw drawing name or an application-owned term.</summary>
/// <param name="Text">Value text.</param>
/// <param name="IsApplicationText">Whether localization may translate the term.</param>
public sealed record DrawingTextValue(string Text, bool IsApplicationText = false) : DrawingValue;

/// <summary>A numeric measurement with explicit display units.</summary>
/// <param name="Value">Raw number; angles use radians.</param>
/// <param name="Unit">Measurement kind.</param>
public sealed record DrawingNumberValue(double Value, DrawingUnit Unit) : DrawingValue;

/// <summary>A boolean primitive property.</summary>
/// <param name="Value">Property state.</param>
public sealed record DrawingBooleanValue(bool Value) : DrawingValue;

/// <summary>An assigned color value.</summary>
/// <param name="Color">Native assignment identity.</param>
public sealed record DrawingColorValue(AssignedColor Color) : DrawingValue;

/// <summary>An assigned lineweight value.</summary>
/// <param name="Lineweight">Native assignment identity.</param>
public sealed record DrawingLineweightValue(AssignedLineweight Lineweight) : DrawingValue;

/// <summary>An assigned transparency value.</summary>
/// <param name="Transparency">Native assignment identity.</param>
public sealed record DrawingTransparencyValue(AssignedTransparency Transparency) : DrawingValue;

/// <summary>A layer name whose grouping identity remains the layer identifier.</summary>
/// <param name="Id">Layer identity.</param>
/// <param name="Name">Drawing-provided layer name.</param>
public sealed record DrawingLayerValue(ILayerId Id, string Name) : DrawingValue
{
    /// <summary>Layer names describe the identity without participating in grouping equality.</summary>
    /// <param name="other">Layer value to compare.</param>
    public bool Equals(DrawingLayerValue? other) => other is not null && Id.Equals(other.Id);

    /// <inheritdoc />
    public override int GetHashCode() => Id.GetHashCode();
}

/// <summary>Units attached to numeric primitive facts.</summary>
public enum DrawingUnit
{
    /// <summary>A number of items.</summary>
    Count,
    /// <summary>A distance in drawing units.</summary>
    Distance,
    /// <summary>An angle stored in radians.</summary>
    Angle,
    /// <summary>A dimensionless factor.</summary>
    Scale
}

/// <summary>A selected property and its available value.</summary>
/// <param name="Id">Stable property identity.</param>
/// <param name="Value">Value, or null when unavailable.</param>
public sealed record DrawingProperty(DrawingPropertyId Id, DrawingValue? Value);

/// <summary>The meaningful row measurement, separate from the number of placed targets.</summary>
/// <param name="Id">Measurement identity used for the column header.</param>
/// <param name="Value">Measurement, or null when unavailable.</param>
public sealed record DrawingMetric(DrawingPropertyId Id, DrawingNumberValue? Value);