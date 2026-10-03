using Common;

namespace CadLens.Lenses;

/// <summary>An entity's identifier and the two values used to group it.</summary>
/// <param name="Id">Placed object identity.</param>
/// <param name="LayerId">Assigned layer identity.</param>
/// <param name="TypeKey">Runtime type used for grouping.</param>
public sealed record EntitySnapshot(IPlacedObjectId Id, ILayerId LayerId, string TypeKey);
