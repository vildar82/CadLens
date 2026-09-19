using Autodesk.AutoCAD.DatabaseServices;

namespace CadLens.AutoCAD;

internal interface IVerificationGraphics
{
    void Apply(Database database, ObjectId[] targets, ObjectId[] inventory);
    void Clear(bool redraw = true);
}