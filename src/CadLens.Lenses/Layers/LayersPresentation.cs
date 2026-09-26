using System.Collections.Immutable;

namespace CadLens.Lenses;

/// <summary>Detached inventory and display options for the Layers explorer.</summary>
/// <param name="Label">Lens title.</param>
/// <param name="SpaceLabel">Active-space description.</param>
/// <param name="Groups">Root groups.</param>
/// <param name="Filters">Lens-provided boolean options.</param>
/// <param name="EmptyMessage">Explanation for an empty result.</param>
public sealed record LayersPresentation(
    string Label,
    string SpaceLabel,
    ImmutableArray<LensNode> Groups,
    ImmutableArray<BooleanFilter> Filters,
    string EmptyMessage);