using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;
using Trace = System.Diagnostics.Trace;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace Common.AutoCAD;

/// <summary>Applies temporary colors through a drawable overrule without modifying the drawing.</summary>
/// <param name="options">Colors supplied by the calling application.</param>
public sealed class EntityHighlightService(EntityHighlightOptions options) : DrawableOverrule, IEntityHighlightService
{
    private HashSet<ObjectId> _targets = [];
    private HashSet<ObjectId> _inventory = [];
    private Dictionary<ObjectId, Hatch> _dimmedHatches = [];
    private Database? _database;
    private bool _registered;

    /// <inheritdoc />
    public void Apply(Database database, ObjectId[] targets, ObjectId[] inventory)
    {
        var targetIds = targets.ToHashSet();
        var nextHatches = CreateDimmedHatches(database, targetIds, inventory);
        var previousHatches = _dimmedHatches;

        _database = database;
        _targets = targetIds;
        _inventory = [.. inventory];
        _dimmedHatches = nextHatches;

        try
        {
            if (!_registered)
            {
                SetCustomFilter();
                AddOverrule(GetClass(typeof(Entity)), this, false);
                _registered = true;
                Overruling = true;
            }

            RegenerateAllViewports(Application.DocumentManager.MdiActiveDocument);
        }
        catch
        {
            if (!_registered)
            {
                _database = null;
                _targets = [];
                _inventory = [];
                _dimmedHatches = [];
                DisposeHatches(nextHatches);
            }

            throw;
        }
        finally
        {
            DisposeHatches(previousHatches);
        }
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
    public override bool WorldDraw(Drawable drawable, WorldDraw worldDraw)
    {
        if (drawable is Hatch hatch && _dimmedHatches.TryGetValue(hatch.ObjectId, out var clone))
        {
            try
            {
                return clone.WorldDraw(worldDraw);
            }
            catch (System.Exception exception)
            {
                Trace.TraceError("CAD Lens hatch WorldDraw failed: {0}", exception);
            }
        }

        return base.WorldDraw(drawable, worldDraw);
    }

    /// <inheritdoc />
    public override void ViewportDraw(Drawable drawable, ViewportDraw viewportDraw)
    {
        if (drawable is Hatch hatch && _dimmedHatches.TryGetValue(hatch.ObjectId, out var clone))
        {
            try
            {
                clone.ViewportDraw(viewportDraw);
                return;
            }
            catch (System.Exception exception)
            {
                Trace.TraceError("CAD Lens hatch ViewportDraw failed: {0}", exception);
            }
        }

        base.ViewportDraw(drawable, viewportDraw);
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
        var hatches = _dimmedHatches;
        _dimmedHatches = [];
        var affectedDatabase = _database;
        _database = null;

        // Do not set Overruling=false: other plugins may own registered overrules.
        try
        {
            if (redraw)
            {
                var document = Application.DocumentManager.MdiActiveDocument;

                if (document is not null && document.Database == affectedDatabase)
                    RegenerateAllViewports(document);
            }
        }
        finally
        {
            DisposeHatches(hatches);
        }
    }

    private Dictionary<ObjectId, Hatch> CreateDimmedHatches(Database database, HashSet<ObjectId> targets, ObjectId[] inventory)
    {
        var clones = new Dictionary<ObjectId, Hatch>();

        try
        {
            using var transaction = database.TransactionManager.StartTransaction();

            foreach (var id in inventory)
            {
                if (targets.Contains(id) || !id.IsValid || id.IsErased || id.ObjectClass != GetClass(typeof(Hatch)))
                    continue;

                try
                {
                    if (transaction.GetObject(id, OpenMode.ForRead) is not Hatch hatch)
                        continue;

                    var clone = (Hatch)hatch.Clone();

                    try
                    {
                        clone.Color = Color.FromRgb(options.Dimmed.Red, options.Dimmed.Green, options.Dimmed.Blue);

                        if (hatch.BackgroundColor.ColorMethod != ColorMethod.None)
                            clone.BackgroundColor = Color.FromRgb(
                                options.DimmedHatchBackground.Red,
                                options.DimmedHatchBackground.Green,
                                options.DimmedHatchBackground.Blue);

                        clones.Add(id, clone);
                    }
                    catch
                    {
                        clone.Dispose();
                        throw;
                    }
                }
                catch (System.Exception exception)
                {
                    Trace.TraceError("CAD Lens could not prepare hatch {0}: {1}", id, exception);
                }
            }

            return clones;
        }
        catch
        {
            DisposeHatches(clones);
            throw;
        }
    }

    private static void DisposeHatches(Dictionary<ObjectId, Hatch> hatches)
    {
        foreach (var hatch in hatches.Values)
            hatch.Dispose();
    }

    private static void RegenerateAllViewports(Document document)
    {
        // ActiveX AcRegenType.acAllViewports; Editor.Regen refreshes only the active view.
        const int allViewports = 1;
        dynamic drawing = document.GetAcadDocument();
        drawing.Regen(allViewports);
    }
}
