using CadLens.Common;
using System.Collections.Immutable;

namespace CadLens.Lenses;

/// <summary>A generic group or object with immutable presentation and target data.</summary>
/// <param name="Id">Stable identity within the parent.</param>
/// <param name="Label">User-facing name.</param>
/// <param name="Objects">Document-scoped targets contributing to the count.</param>
/// <param name="Children">Groups or individual objects at the next level.</param>
/// <param name="Fields">Lens-specific details.</param>
/// <param name="Actions">Available host actions.</param>
/// <param name="Kind">Drawing node role, or generic group for other lenses.</param>
/// <param name="TypeKey">Runtime type owning type-scoped grouping controls.</param>
/// <param name="Properties">Composite grouping values.</param>
/// <param name="RowMetric">Object row measurement or the parent column identity.</param>
/// <param name="DisplayColor">Resolved RGB row color, or null for a neutral border.</param>
public sealed record LensNode(
    string Id,
    string Label,
    ImmutableArray<IPlacedObjectId> Objects,
    ImmutableArray<LensNode> Children,
    ImmutableArray<DetailField> Fields,
    ImmutableArray<LensAction> Actions,
    LensNodeKind Kind = LensNodeKind.GenericGroup,
    string? TypeKey = null,
    ImmutableArray<DrawingProperty> Properties = default,
    DrawingMetric? RowMetric = null,
    int? DisplayColor = null)
{
    /// <summary>Number of objects represented by this node.</summary>
    public int Count => Objects.Length;
}

/// <summary>Finite drawing roles used by the shared explorer.</summary>
public enum LensNodeKind
{
    /// <summary>A group supplied by another lens.</summary>
    GenericGroup,
    /// <summary>A drawing layer.</summary>
    Layer,
    /// <summary>A primitive runtime type.</summary>
    Type,
    /// <summary>One combination of primitive properties.</summary>
    PropertyGroup,
    /// <summary>One placed drawing entity.</summary>
    Object
}
