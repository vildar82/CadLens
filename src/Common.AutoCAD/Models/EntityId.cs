using Autodesk.AutoCAD.DatabaseServices;
namespace Common.AutoCAD;

/// <summary>Identity of an entity placed in an AutoCAD model or layout.</summary>
/// <param name="NativeId">AutoCAD object identifier.</param>
public sealed record EntityId(ObjectId NativeId) : IPlacedObjectId
{
    /// <inheritdoc />
    public string DisplayId => NativeId.Handle.ToString();
}
