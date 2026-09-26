using Autodesk.AutoCAD.DatabaseServices;

namespace Common.AutoCAD;

/// <summary>Owns temporary visual isolation. The caller clears it before leaving the drawing context.</summary>
public interface IEntityIsolationService
{
    /// <summary>Suppresses non-target inventory graphics in the active document.</summary>
    /// <param name="database">Active document database.</param>
    /// <param name="targets">Entities to keep visible.</param>
    /// <param name="inventory">Entities affected by the effect.</param>
    void Apply(Database database, ObjectId[] targets, ObjectId[] inventory);

    /// <summary>Removes the effect.</summary>
    /// <param name="redraw">Whether to regenerate the affected active document.</param>
    void Clear(bool redraw = true);
}
