using Autodesk.AutoCAD.DatabaseServices;

namespace Common.AutoCAD;

/// <summary>Owns temporary entity colors. The caller clears them before leaving the drawing context.</summary>
public interface IEntityHighlightService
{
    /// <summary>Colors targets and dims the remaining inventory in the active document.</summary>
    /// <param name="database">Active document database.</param>
    /// <param name="targets">Entities to emphasize.</param>
    /// <param name="inventory">Entities affected by the effect.</param>
    void Apply(Database database, ObjectId[] targets, ObjectId[] inventory);

    /// <summary>Removes the effect.</summary>
    /// <param name="redraw">Whether to regenerate the affected active document.</param>
    void Clear(bool redraw = true);
}