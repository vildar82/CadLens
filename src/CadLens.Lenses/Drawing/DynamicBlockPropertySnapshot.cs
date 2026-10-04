namespace CadLens.Lenses;

/// <summary>One detached dynamic property of a block insertion.</summary>
/// <param name="Name">Exact drawing-owned property name, or null when unreadable.</param>
/// <param name="Value">Typed property value, or null when unavailable.</param>
public sealed record DynamicBlockPropertySnapshot(string? Name, DrawingValue? Value);
