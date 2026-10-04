using System.Globalization;
using CadLens.Common;

namespace CadLens.Lenses;

/// <summary>Drawing precision shared by numeric display and grouping; stored values remain exact.</summary>
/// <param name="Linear">Fractional digits for linear measurements.</param>
/// <param name="Angular">Fractional digits for angular measurements.</param>
public sealed record DrawingPrecision(int Linear = 4, int Angular = 0)
{
    /// <summary>Default precision when a presentation does not supply drawing settings.</summary>
    public static DrawingPrecision Default { get; } = new();

    /// <summary>Rounds a raw measurement in display units; angles are returned in degrees.</summary>
    /// <param name="value">Original measurement, with angles in radians.</param>
    /// <param name="unit">Measurement kind.</param>
    public double Round(double value, DrawingUnit unit)
    {
        if (unit == DrawingUnit.Angle)
            value *= 180 / Math.PI;

        var rounded = Math.Round(value, GetDigits(unit), MidpointRounding.AwayFromZero);

        if (!rounded.IsFinite())
            return rounded;

        var text = rounded.ToString(GetNumberFormat(unit), CultureInfo.InvariantCulture);

        if (!double.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var displayed) || !displayed.IsFinite())
            return rounded < 0 ? double.MinValue : double.MaxValue;

        return displayed == 0 ? 0 : displayed;
    }

    /// <summary>Returns the numeric format shared by captions and grouping keys.</summary>
    /// <param name="unit">Measurement kind.</param>
    public string GetNumberFormat(DrawingUnit unit)
    {
        var digits = GetDigits(unit);

        return unit == DrawingUnit.Count ? "N0" : digits == 0 ? "0" : $"0.{new string('#', digits)}";
    }

    private int GetDigits(DrawingUnit unit) => Math.Min(Math.Max(unit switch
    {
        DrawingUnit.Count => 0,
        DrawingUnit.Angle => Angular,
        _ => Linear
    }, 0), 8);
}