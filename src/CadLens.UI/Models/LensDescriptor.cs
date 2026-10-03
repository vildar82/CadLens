namespace CadLens.UI;

/// <summary>Identifies a registered lens without reading drawing data.</summary>
/// <param name="Id">Stable identity, unique among registered lenses.</param>
/// <param name="Label">Title shown in the lens bar.</param>
/// <param name="Description">App-owned explanation shown in the lens tooltip.</param>
public sealed record LensDescriptor(string Id, string Label, string Description);
