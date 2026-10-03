using System.Collections.Immutable;
using Common;

namespace CadLens.Lenses;

/// <summary>One placed entity's detached identity and immutable observed properties.</summary>
/// <param name="Id">Placed object identity.</param>
/// <param name="LayerId">Assigned layer identity.</param>
/// <param name="TypeKey">Runtime type used for grouping.</param>
/// <param name="Properties">Observed primitive properties; a present null value means unavailable.</param>
/// <param name="PrimaryMetric">Property used as the meaningful row measurement, when supported.</param>
public sealed record EntitySnapshot(
    IPlacedObjectId Id,
    ILayerId LayerId,
    string TypeKey,
    ImmutableDictionary<DrawingPropertyId, DrawingValue?>? Properties = null,
    DrawingPropertyId? PrimaryMetric = null);
