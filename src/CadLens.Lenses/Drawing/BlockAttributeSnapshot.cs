namespace CadLens.Lenses;

/// <summary>One attached block attribute, detached from its insertion and native transaction.</summary>
/// <param name="Tag">Drawing-owned tag, or null when unavailable.</param>
/// <param name="Value">Complete text value; empty means blank and null means unavailable.</param>
public sealed record BlockAttributeSnapshot(string? Tag, string? Value);