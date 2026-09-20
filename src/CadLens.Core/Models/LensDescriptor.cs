namespace CadLens.Core;

/// <summary>Identifies a registered lens without reading drawing data.</summary>
/// <param name="Id">Stable identity, unique among registered lenses.</param>
/// <param name="Label">Title shown in the lens bar.</param>
public sealed record LensDescriptor(string Id, string Label);