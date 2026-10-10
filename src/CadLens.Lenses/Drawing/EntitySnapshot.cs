using System.Collections.Immutable;
using CadLens.Common;

namespace CadLens.Lenses;

/// <summary>
/// Stores one placed entity's detached identity and immutable observed properties.
/// </summary>
/// <param name="Id">Placed object identity.</param>
/// <param name="LayerId">Assigned layer identity.</param>
/// <param name="TypeKey">Runtime type used for grouping.</param>
/// <param name="Properties">Observed built-in, attribute, and dynamic properties keyed by source and identity; null values are unavailable.</param>
/// <param name="PrimaryMetric">Property used as the meaningful row measurement, when supported.</param>
/// <param name="DisplayColor">Explicit RGB color; inherited or unavailable colors remain null.</param>
public sealed record EntitySnapshot(
    IPlacedObjectId Id,
    ILayerId LayerId,
    string TypeKey,
    ImmutableDictionary<DrawingPropertyKey, DrawingValue?>? Properties = null,
    DrawingPropertyId? PrimaryMetric = null,
    int? DisplayColor = null);
