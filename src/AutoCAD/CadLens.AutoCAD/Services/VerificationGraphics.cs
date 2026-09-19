using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.GraphicsInterface;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadLens.AutoCAD;

// Candidate for design decision 6; block/text and viewport-isolation checks remain mandatory.
internal sealed class VerificationGraphics : DrawableOverrule, IVerificationGraphics
{
    private static readonly EntityColor Accent = new(70, 210, 230);
    private static readonly EntityColor Dimmed = new(65, 72, 80);
    private HashSet<ObjectId> _targets = [];
    private Database? _database;
    private bool _registered;

    public void Apply(Database database, ObjectId[] targets, ObjectId[] inventory)
    {
        Clear(redraw: false);
        _database = database;
        _targets = [.. targets];
        SetIdFilter(inventory);
        AddOverrule(GetClass(typeof(Entity)), this, false);
        _registered = true;
        Overruling = true;
        Application.DocumentManager.MdiActiveDocument.Editor.Regen();
    }

    public override int SetAttributes(Drawable drawable, DrawableTraits traits)
    {
        var flags = base.SetAttributes(drawable, traits);

        if (drawable is Entity entity && entity.Database == _database && traits is SubEntityTraits subTraits)
            subTraits.TrueColor = _targets.Contains(entity.ObjectId) ? Accent : Dimmed;

        return flags;
    }

    public void Clear(bool redraw = true)
    {
        if (!_registered)
            return;

        RemoveOverrule(GetClass(typeof(Entity)), this);

        _registered = false;
        _targets.Clear();
        var affectedDatabase = _database;
        _database = null;

        // Do not set Overruling=false: other plugins may own registered overrules.
        if (!redraw)
            return;

        var document = Application.DocumentManager.MdiActiveDocument;

        if (document is not null && document.Database == affectedDatabase)
            document.Editor.Regen();
    }
}