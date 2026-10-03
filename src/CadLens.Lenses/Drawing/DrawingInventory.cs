using System.Collections.Immutable;

namespace CadLens.Lenses;

/// <summary>Layer and entity data read from the current space.</summary>
/// <param name="SpaceLabel">User-facing space description.</param>
/// <param name="Layers">Layer metadata.</param>
/// <param name="Entities">Direct active-space entities.</param>
/// <param name="Precision">Drawing display precision, without rounding detached values.</param>
public sealed record DrawingInventory(
    string SpaceLabel,
    ImmutableArray<LayerSnapshot> Layers,
    ImmutableArray<EntitySnapshot> Entities,
    DrawingPrecision? Precision = null);