using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadLens.Common.AutoCAD;

/// <summary>Temporarily hides non-target graphics without changing drawing properties.</summary>
public sealed class EntityIsolationService : DrawableOverrule, IEntityIsolationService
{
    private HashSet<ObjectId> _hidden = [];
    private Database? _database;
    private bool _registered;

    /// <inheritdoc />
    public void Apply(Database database, ObjectId[] targets, ObjectId[] inventory)
    {
        _database = database;
        _hidden = [.. inventory.Except(targets)];

        try
        {
            if (!_registered)
            {
                SetCustomFilter();
                AddOverrule(GetClass(typeof(Entity)), this, false);
                _registered = true;
                Overruling = true;
            }

            RegenerateAllViewports(
                Application.DocumentManager.MdiActiveDocument ??
                throw new InvalidOperationException("The active drawing is no longer available."));
        }
        catch
        {
            Clear(redraw: false);
            throw;
        }
    }

    /// <inheritdoc />
    public Action Suspend()
    {
        var document = Application.DocumentManager.MdiActiveDocument;

        if (document is null || document.Database != _database)
            return () => { };

        var database = document.Database;
        var space = database.CurrentSpaceId;
        var hidden = _hidden;
        HashSet<ObjectId> suspended = [];
        _hidden = suspended;

        void Restore()
        {
            if (document != Application.DocumentManager.MdiActiveDocument || database.CurrentSpaceId != space ||
                !ReferenceEquals(_hidden, suspended))
                return;

            _hidden = hidden;
            RegenerateAllViewports(document);
        }

        try
        {
            RegenerateAllViewports(document);
            return Restore;
        }
        catch
        {
            Restore();
            throw;
        }
    }

    /// <inheritdoc />
    public override bool IsApplicable(RXObject overruledSubject) =>
        overruledSubject is Entity entity && entity.Database == _database && _hidden.Contains(entity.ObjectId);

    /// <inheritdoc />
    public override int SetAttributes(Drawable drawable, DrawableTraits traits) =>
        base.SetAttributes(drawable, traits) | (int) DrawableAttributes.IsInvisible;

    /// <inheritdoc />
    public void Clear(bool redraw = true)
    {
        if (_registered)
        {
            RemoveOverrule(GetClass(typeof(Entity)), this);
            _registered = false;
        }

        var affectedDatabase = _database;
        _database = null;
        _hidden = [];

        if (!redraw)
            return;

        var document = Application.DocumentManager.MdiActiveDocument;

        if (document is not null && document.Database == affectedDatabase)
            RegenerateAllViewports(document);
    }

    private static void RegenerateAllViewports(Document document)
    {
        const int allViewports = 1;
        dynamic drawing = document.GetAcadDocument();
        drawing.Regen(allViewports);
    }
}