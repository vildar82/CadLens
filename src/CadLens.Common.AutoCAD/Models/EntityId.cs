using Autodesk.AutoCAD.DatabaseServices;

namespace CadLens.Common.AutoCAD;

/// <summary>Identity of an entity placed in an AutoCAD model or layout.</summary>
/// <param name="NativeId">AutoCAD object identifier.</param>
/// <param name="Container">Containing block occurrence, or null for a direct space object.</param>
public sealed record EntityId(ObjectId NativeId, EntityId? Container = null) : IPlacedObjectId
{
    /// <inheritdoc />
    public string DisplayId => Container is null
        ? NativeId.Handle.ToString()
        : $"{Container.DisplayId}/{NativeId.Handle}";

    /// <summary>Top-level insertion or direct entity in the current space.</summary>
    public ObjectId RootId => Container?.RootId ?? NativeId;
}
