using System.Collections.Immutable;

namespace CadLens.Common;

/// <summary>Placed objects drawn in one temporary color.</summary>
/// <param name="Objects">Objects to color.</param>
/// <param name="Color">Temporary RGB color.</param>
public sealed record PaintGroup(ImmutableArray<IPlacedObjectId> Objects, int Color);