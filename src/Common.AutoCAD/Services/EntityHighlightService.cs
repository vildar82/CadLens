using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace Common.AutoCAD;

/// <summary>Applies temporary colors through a drawable overrule without modifying the drawing.</summary>
/// <param name="options">Colors supplied by the calling application.</param>
public sealed class EntityHighlightService(EntityHighlightOptions options) : DrawableOverrule, IEntityHighlightService
{
    private HashSet<ObjectId> _targets = [];
    private HashSet<ObjectId> _inventory = [];
    private Database? _database;
    private bool _registered;

    /// <inheritdoc />
    public void Apply(Database database, ObjectId[] targets, ObjectId[] inventory)
    {
        Clear(redraw: false);
        _database = database;
        _targets = [.. targets];
        _inventory = [.. inventory];
        SetCustomFilter();
        AddOverrule(GetClass(typeof(Entity)), this, false);
        _registered = true;
        Overruling = true;

        Application.DocumentManager.MdiActiveDocument.Editor.Regen();
    }

    /// <inheritdoc />
    public override bool IsApplicable(RXObject overruledSubject) =>
        overruledSubject is Entity entity && _inventory.Contains(entity.ObjectId);

    /// <inheritdoc />
    public override int SetAttributes(Drawable drawable, DrawableTraits traits)
    {
        var drawableFlags = base.SetAttributes(drawable, traits);

        if (drawable is Entity entity && entity.Database == _database && traits is SubEntityTraits subTraits)
            subTraits.TrueColor = _targets.Contains(entity.ObjectId) ? options.Accent : options.Dimmed;

        return drawableFlags;
    }

    /// <inheritdoc />
    public void Clear(bool redraw = true)
    {
        if (!_registered)
            return;

        RemoveOverrule(GetClass(typeof(Entity)), this);

        _registered = false;
        _targets = [];
        _inventory = [];
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