namespace CadLens.Lenses;

/// <summary>A lens-specific inclusion option; sessions start with all options disabled.</summary>
/// <param name="Id">Stable option identity.</param>
/// <param name="Label">User-facing label.</param>
/// <param name="Description">Explanation displayed as a tooltip.</param>
/// <param name="Icon">Semantic icon role.</param>
public sealed record BooleanFilter(string Id, string Label, string Description, IconRole Icon);