using Autodesk.AutoCAD.DatabaseServices;

namespace Common.AutoCAD;

/// <summary>Convenient access to the current drawing space.</summary>
public static class DatabaseExtensions
{
    /// <summary>Opens the database's current space in the active transaction.</summary>
    /// <param name="database">Database with an active regular transaction.</param>
    public static BlockTableRecord GetActiveSpace(this Database database) =>
        database.CurrentSpaceId.GetObject<BlockTableRecord>()!;
}