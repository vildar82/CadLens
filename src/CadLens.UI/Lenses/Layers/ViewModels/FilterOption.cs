using CadLens.Lenses;

namespace CadLens.UI;

/// <summary>A generic lens option and its current presentation state.</summary>
/// <param name="Descriptor">Lens-provided label, tooltip and icon.</param>
/// <param name="IsEnabled">Whether hidden objects matching this option are included.</param>
public sealed record FilterOption(BooleanFilter Descriptor, bool IsEnabled);