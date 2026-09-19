using System.Collections.Immutable;

namespace CadLens.Core;

/// <summary>A generic group or object with immutable presentation and target data.</summary>
/// <param name="Id">Stable identity within the parent.</param>
/// <param name="Label">User-facing name.</param>
/// <param name="Objects">Document-scoped targets contributing to the count.</param>
/// <param name="Children">Groups or individual objects at the next level.</param>
/// <param name="Fields">Lens-specific details.</param>
/// <param name="Actions">Available host actions.</param>
public sealed record LensNode(
    string Id,
    string Label,
    ImmutableArray<HostObjectId> Objects,
    ImmutableArray<LensNode> Children,
    ImmutableArray<DetailField> Fields,
    ImmutableArray<LensAction> Actions)
{
    /// <summary>Number of objects represented by this node.</summary>
    public int Count => Objects.Length;
}