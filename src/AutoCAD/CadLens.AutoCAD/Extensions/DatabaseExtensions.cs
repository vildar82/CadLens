using Autodesk.AutoCAD.DatabaseServices;

namespace CadLens.AutoCAD;

/// <summary>Convenient access to the current drawing space.</summary>
internal static class DatabaseExtensions
{
    /// <summary>Opens the database's current space in the active transaction.</summary>
    internal static BlockTableRecord GetActiveSpace(this Database database) =>
        database.CurrentSpaceId.GetObject<BlockTableRecord>()!;
}