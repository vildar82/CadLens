using System.Collections.Immutable;
using CadLens.Common;

namespace CadLens.Lenses;

/// <summary>One placed entity's detached identity and immutable observed properties.</summary>
/// <param name="Id">Placed object identity.</param>
/// <param name="LayerId">Assigned layer identity.</param>
/// <param name="TypeKey">Runtime type used for grouping.</param>
/// <param name="Properties">Observed primitive properties; a present null value means unavailable.</param>
/// <param name="PrimaryMetric">Property used as the meaningful row measurement, when supported.</param>
/// <param name="BlockAttributes">Attached insertion attributes; default means unavailable or not a block.</param>
/// <param name="DynamicBlockProperties">Detached dynamic insertion properties; default means unavailable.</param>
/// <param name="DisplayColor">Explicit RGB color; inherited or unavailable colors remain null.</param>
public sealed record EntitySnapshot(
    IPlacedObjectId Id,
    ILayerId LayerId,
    string TypeKey,
    ImmutableDictionary<DrawingPropertyId, DrawingValue?>? Properties = null,
    DrawingPropertyId? PrimaryMetric = null,
    ImmutableArray<BlockAttributeSnapshot> BlockAttributes = default,
    ImmutableArray<DynamicBlockPropertySnapshot> DynamicBlockProperties = default,
    int? DisplayColor = null);
