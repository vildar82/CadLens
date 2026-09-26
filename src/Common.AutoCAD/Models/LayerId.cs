using Autodesk.AutoCAD.DatabaseServices;
namespace Common.AutoCAD;

/// <summary>Identity of an AutoCAD layer.</summary>
/// <param name="NativeId">AutoCAD object identifier.</param>
public sealed record LayerId(ObjectId NativeId) : ILayerId
{
    /// <inheritdoc />
    public string DisplayId => NativeId.Handle.ToString();
}
