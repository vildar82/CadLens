using System.Collections.Immutable;

namespace CadLens.Core;

/// <summary>A lens result that can be rendered without host or feature types.</summary>
/// <param name="LensId">Lens identity.</param>
/// <param name="Label">Lens title.</param>
/// <param name="SpaceLabel">Active-space description.</param>
/// <param name="Groups">Root groups.</param>
/// <param name="Filters">Lens-provided boolean options.</param>
/// <param name="EmptyMessage">Explanation for an empty result.</param>
public sealed record LensPresentation(
    string LensId,
    string Label,
    string SpaceLabel,
    ImmutableArray<LensNode> Groups,
    ImmutableArray<BooleanFilter> Filters,
    string EmptyMessage);