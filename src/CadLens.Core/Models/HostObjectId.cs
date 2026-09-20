namespace CadLens.Core;

/// <summary>Wraps the host's object identifier without referencing its API from shared code.</summary>
/// <param name="Value">The native identifier, such as an AutoCAD ObjectId.</param>
public sealed record HostObjectId(object Value)
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString() ?? string.Empty;
}