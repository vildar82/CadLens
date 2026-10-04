namespace CadLens.Common;

/// <summary>Checks numeric values across supported host frameworks.</summary>
public static class NumberExtensions
{
    /// <summary>Returns whether a value is neither NaN nor infinity.</summary>
    /// <param name="value">Number to check.</param>
    public static bool IsFinite(this double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}