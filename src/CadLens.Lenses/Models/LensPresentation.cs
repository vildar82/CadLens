using System.Collections.Immutable;

namespace CadLens.Lenses;

/// <summary>Detached inventory and display options for the drawing explorer.</summary>
/// <param name="Label">Lens title.</param>
/// <param name="SpaceLabel">Active-space description.</param>
/// <param name="Groups">Root groups.</param>
/// <param name="Filters">Lens-provided boolean options.</param>
/// <param name="EmptyMessage">Explanation for an empty result.</param>
/// <param name="RootLabel">Root breadcrumb label.</param>
/// <param name="SearchPlaceholder">Root search prompt.</param>
/// <param name="GroupLabel">Root group name used in counts.</param>
/// <param name="Inventory">Retained detached facts for local grouping changes.</param>
/// <param name="Precision">Drawing display precision for measurement formatting.</param>
public sealed record LensPresentation(
    string Label,
    string SpaceLabel,
    ImmutableArray<LensNode> Groups,
    ImmutableArray<BooleanFilter> Filters,
    string EmptyMessage,
    string RootLabel,
    string SearchPlaceholder,
    string GroupLabel,
    DrawingInventory? Inventory = null,
    DrawingPrecision? Precision = null);