using Common;

namespace CadLens.Lenses;

/// <summary>A single condition evaluated against raw detached property values.</summary>
/// <param name="PropertyId">Property to inspect.</param>
/// <param name="Operator">Comparison to apply.</param>
/// <param name="Value">Typed comparison value, with angles in radians.</param>
public sealed record DrawingPropertyFilter(
    DrawingPropertyId PropertyId,
    DrawingFilterOperator Operator,
    DrawingValue Value)
{
    /// <summary>Tests an entity without modifying the retained inventory.</summary>
    /// <param name="entity">Detached entity facts.</param>
    /// <param name="layer">Assigned layer facts.</param>
    public bool Matches(EntitySnapshot entity, LayerSnapshot layer) =>
        Matches(DrawingProperties.GetValue(entity, layer, PropertyId));

    /// <summary>Tests a raw value; unavailable and incompatible values never match.</summary>
    /// <param name="actual">Observed value, or null when unavailable.</param>
    public bool Matches(DrawingValue? actual) => (actual, Value) switch
    {
        (DrawingNumberValue first, DrawingNumberValue second) => MatchesNumber(first, second),
        (DrawingTextValue first, DrawingTextValue second) => Operator == DrawingFilterOperator.Contains
            ? first.Text.IndexOf(second.Text, StringComparison.OrdinalIgnoreCase) >= 0
            : MatchesEquality(string.Equals(first.Text, second.Text, StringComparison.OrdinalIgnoreCase)),
        (DrawingBooleanValue first, DrawingBooleanValue second) => MatchesEquality(first == second),
        (DrawingLayerValue first, DrawingLayerValue second) => MatchesEquality(first == second),
        (DrawingColorValue first, DrawingColorValue second) => MatchesEquality(first == second),
        (DrawingLineweightValue first, DrawingLineweightValue second) => MatchesEquality(first == second),
        (DrawingTransparencyValue first, DrawingTransparencyValue second) => MatchesEquality(first == second),
        _ => false
    };

    private bool MatchesNumber(DrawingNumberValue actual, DrawingNumberValue expected)
    {
        if (!actual.Value.IsFinite() || !expected.Value.IsFinite() || actual.Unit != expected.Unit)
            return false;

        var comparison = actual.Value.CompareTo(expected.Value);

        return Operator switch
        {
            DrawingFilterOperator.Equal => comparison == 0,
            DrawingFilterOperator.NotEqual => comparison != 0,
            DrawingFilterOperator.LessThan => comparison < 0,
            DrawingFilterOperator.LessThanOrEqual => comparison <= 0,
            DrawingFilterOperator.GreaterThan => comparison > 0,
            DrawingFilterOperator.GreaterThanOrEqual => comparison >= 0,
            _ => false
        };
    }

    private bool MatchesEquality(bool equal) => Operator switch
    {
        DrawingFilterOperator.Equal => equal,
        DrawingFilterOperator.NotEqual => !equal,
        _ => false
    };
}

/// <summary>Comparisons supported by a drawing property filter.</summary>
public enum DrawingFilterOperator
{
    /// <summary>Equal raw values.</summary>
    Equal,
    /// <summary>Different available raw values.</summary>
    NotEqual,
    /// <summary>A smaller numeric value.</summary>
    LessThan,
    /// <summary>A smaller or equal numeric value.</summary>
    LessThanOrEqual,
    /// <summary>A larger numeric value.</summary>
    GreaterThan,
    /// <summary>A larger or equal numeric value.</summary>
    GreaterThanOrEqual,
    /// <summary>A case-insensitive text fragment.</summary>
    Contains
}
