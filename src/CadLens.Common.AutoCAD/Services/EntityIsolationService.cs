using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadLens.Common.AutoCAD;

/// <summary>Temporarily hides non-target graphics without changing drawing properties.</summary>
public sealed class EntityIsolationService : DrawableOverrule, IEntityIsolationService
{
    private const int TransientSubMode = 128;

    // Native transient registration does not own managed wrappers. Keep failed removals alive across panel scopes.
    private static readonly HashSet<EntityIsolationService> PendingRemoval = [];
    private HashSet<ObjectId> _hidden = [];
    private List<Entity> _transients = [];
    private readonly HashSet<Entity> _displayed = [];
    private Database? _database;
    private bool _registered;

    /// <inheritdoc />
    public void Apply(Database database, ObjectId[] targets, ObjectId[] inventory)
    {
        Clear(redraw: false);
        _database = database;
        _hidden = [.. inventory.Except(targets)];

        try
        {
            Register();

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
    public int ApplyOccurrences(Database database, EntityId[] targets, ObjectId[] inventory)
    {
        Clear(redraw: false);
        _database = database;

        try
        {
            if (!database.TileMode && targets.Any(id => id.Container is not null))
                throw new NotSupportedException("Isolation inside blocks is available only in model space.");

            HashSet<ObjectId> activeSpace = [.. inventory];
            var direct = new HashSet<ObjectId>();
            var resolved = new List<(EntityId Id, EntityOccurrence Occurrence)>();

            using (var transaction = database.TransactionManager.StartTransaction())
            {
                foreach (var target in targets.Distinct())
                {
                    if (!activeSpace.Contains(target.RootId))
                        continue;

                    var occurrence = EntityOccurrence.Resolve(database, target);

                    if (occurrence is null)
                        continue;

                    resolved.Add((target, occurrence));

                    if (target.Container is null)
                        direct.Add(target.NativeId);
                }

                foreach (var (id, occurrence) in resolved)
                {
                    if (id.Container is not null && !direct.Contains(id.RootId))
                        occurrence.AddGraphics(_transients);
                }

                transaction.Commit();
            }

            if (resolved.Count == 0)
            {
                RegenerateAllViewports(Application.DocumentManager.MdiActiveDocument!);
                return 0;
            }

            _database = database;
            _hidden = [.. inventory.Except(direct)];
            Register();
            ShowTransients();
            RegenerateAllViewports(Application.DocumentManager.MdiActiveDocument!);
            return resolved.Count;
        }
        catch
        {
            ClearAfterFailure();
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
            if (!ReferenceEquals(_hidden, suspended))
                return;

            if (document != Application.DocumentManager.MdiActiveDocument || database.CurrentSpaceId != space)
            {
                Clear(redraw: false);
                return;
            }

            _hidden = hidden;

            try
            {
                ShowTransients();
                RegenerateAllViewports(document);
            }
            catch
            {
                if (_transients.Count > 0)
                    ClearAfterFailure();

                throw;
            }
        }

        try
        {
            EraseTransients();
            RegenerateAllViewports(document);
            return Restore;
        }
        catch
        {
            if (_transients.Count > 0)
                ClearAfterFailure();
            else
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
        System.Exception? failure = null;
        EntityIsolationService[] owners = [.. PendingRemoval.Where(owner => owner != this), this];

        foreach (var owner in owners)
        {
            try
            {
                owner.ClearOwnedGraphics(redraw && owner == this);
            }
            catch (System.Exception exception)
            {
                failure ??= exception;
            }
        }

        if (failure is not null)
            throw new InvalidOperationException("Temporary isolation cleanup failed.", failure);
    }

    private void ClearOwnedGraphics(bool redraw)
    {
        var affectedDatabase = _database;
        _database = null;
        _hidden = [];
        var transients = _transients;
        _transients = [];
        System.Exception? failure = null;

        try
        {
            EraseTransients();
        }
        catch (System.Exception exception)
        {
            failure = exception;
        }

        foreach (var transient in transients)
        {
            if (_displayed.Contains(transient))
            {
                _transients.Add(transient);
                continue;
            }

            try
            {
                transient.Dispose();
            }
            catch (System.Exception exception)
            {
                failure ??= exception;
            }
        }

        if (_registered)
        {
            try
            {
                RemoveOverrule(GetClass(typeof(Entity)), this);
                _registered = false;
            }
            catch (System.Exception exception)
            {
                failure ??= exception;
            }
        }

        if (_displayed.Count > 0 || _registered)
            PendingRemoval.Add(this);
        else
            PendingRemoval.Remove(this);

        var document = Application.DocumentManager.MdiActiveDocument;

        if (redraw && document is not null && document.Database == affectedDatabase)
            RegenerateAllViewports(document);

        if (failure is not null)
            throw new InvalidOperationException("Temporary isolation cleanup failed.", failure);
    }

    private void Register()
    {
        if (_registered)
            return;

        SetCustomFilter();
        AddOverrule(GetClass(typeof(Entity)), this, false);
        _registered = true;
        Overruling = true;
    }

    private void ShowTransients()
    {
        var viewports = new IntegerCollection();

        foreach (var transient in _transients)
        {
            _displayed.Add(transient);

            if (TransientManager.CurrentTransientManager.AddTransient(
                    transient,
                    TransientDrawingMode.Main,
                    TransientSubMode,
                    viewports))
                continue;

            _displayed.Remove(transient);
            throw new InvalidOperationException("The nested object graphics could not be displayed.");
        }
    }

    private void EraseTransients()
    {
        var viewports = new IntegerCollection();
        System.Exception? failure = null;

        foreach (var transient in _displayed.ToArray())
        {
            try
            {
                if (!TransientManager.CurrentTransientManager.EraseTransient(transient, viewports))
                    throw new InvalidOperationException("The nested object graphics could not be removed.");

                _displayed.Remove(transient);
            }
            catch (System.Exception exception)
            {
                failure ??= exception;
            }
        }

        if (failure is not null)
            throw new InvalidOperationException("The nested object graphics could not be removed.", failure);
    }

    private void ClearAfterFailure()
    {
        try
        {
            Clear();
        }
        catch (System.Exception)
        {
            // Cleanup attempts every resource; native redraw failures must not preserve a hiding rule.
        }
    }

    private static void RegenerateAllViewports(Document document)
    {
        const int allViewports = 1;
        dynamic drawing = document.GetAcadDocument();
        drawing.Regen(allViewports);
    }
}