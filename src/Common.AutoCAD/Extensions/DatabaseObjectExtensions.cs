using Autodesk.AutoCAD.DatabaseServices;

namespace Common.AutoCAD;

/// <summary>Opens typed objects in the caller's active transaction, skipping erased or invalid identifiers.</summary>
public static class DatabaseObjectExtensions
{
    /// <summary>Opens for reading by default; pass true to open for writing. Requires a regular transaction created with StartTransaction().</summary>
    /// <typeparam name="T">Required object type.</typeparam>
    /// <param name="id">Identifier to open.</param>
    /// <param name="forWrite">Whether to open objects for writing.</param>
    public static T? GetObject<T>(this ObjectId id, bool forWrite = false) where T : DBObject
    {
        if (!id.IsValid || id.IsErased)
            return null;

        var mode = forWrite ? OpenMode.ForWrite : OpenMode.ForRead;

        return id.GetObject(mode, false, true) as T;
    }

    /// <summary>Enumerates typed table records. Materialize the result before the transaction ends.</summary>
    /// <typeparam name="T">Required object type.</typeparam>
    /// <param name="table">Table to enumerate.</param>
    /// <param name="forWrite">Whether to open objects for writing.</param>
    public static IEnumerable<T> GetObjects<T>(this SymbolTable table, bool forWrite = false) where T : DBObject =>
        table.Cast<ObjectId>().Select(id => id.GetObject<T>(forWrite)).OfType<T>();

    /// <summary>Enumerates typed entities directly owned by a block or space, without opening block contents.</summary>
    /// <typeparam name="T">Required object type.</typeparam>
    /// <param name="block">Block or space to enumerate.</param>
    /// <param name="forWrite">Whether to open objects for writing.</param>
    public static IEnumerable<T> GetObjects<T>(this BlockTableRecord block, bool forWrite = false) where T : Entity =>
        block.Cast<ObjectId>().Select(id => id.GetObject<T>(forWrite)).OfType<T>();
}