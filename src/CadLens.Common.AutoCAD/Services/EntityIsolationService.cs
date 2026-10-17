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
    public void Apply(
        Database database,
        ObjectId[] targets,
        ObjectId[] inventory,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Clear(redraw: false, progress);
        _database = database;

        try
        {
            var visible = new HashSet<ObjectId>();

            foreach (var target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                visible.Add(target);
            }

            _hidden = ReadInventory(inventory, cancellationToken, progress);

            foreach (var target in visible)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _hidden.Remove(target);
            }

            cancellationToken.ThrowIfCancellationRequested();
            Register();
            cancellationToken.ThrowIfCancellationRequested();
            RegenerateAllViewports(
                Application.DocumentManager.MdiActiveDocument ??
                throw new InvalidOperationException("The active drawing is no longer available."),
                progress,
                cancellationToken);
        }
        catch
        {
            ClearAfterFailure(progress);
            throw;
        }
    }

    /// <inheritdoc />
    public int ApplyOccurrences(
        Database database,
        EntityId[] targets,
        ObjectId[] inventory,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Clear(redraw: false, progress);
        _database = database;

        try
        {
            var activeSpace = ReadInventory(inventory, cancellationToken, progress);
            var direct = new HashSet<ObjectId>();
            var visited = new HashSet<EntityId>();
            var resolved = new List<(EntityId Id, EntityOccurrence Occurrence)>();
            progress?.Report(new OperationProgress(OperationStage.Isolating, 0, targets.Length));

            using (var transaction = database.TransactionManager.StartTransaction())
            {
                var frozenLayers = EntityOccurrence.ReadFrozenLayers(database, cancellationToken);

                for (var index = 0; index < targets.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var target = targets[index];

                    if (!database.TileMode && target.Container is not null)
                        throw new NotSupportedException("Isolation inside blocks is available only in model space.");

                    if (visited.Add(target) && activeSpace.Contains(target.RootId))
                    {
                        var occurrence = EntityOccurrence.Resolve(database, target, frozenLayers, cancellationToken);

                        if (occurrence is not null)
                        {
                            resolved.Add((target, occurrence));

                            if (target.Container is null)
                                direct.Add(target.NativeId);
                        }
                    }

                    Report(progress, OperationStage.Isolating, index + 1, targets.Length);
                }

                progress?.Report(new OperationProgress(OperationStage.CreatingGraphics, 0, resolved.Count));

                for (var index = 0; index < resolved.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var (id, occurrence) = resolved[index];

                    if (id.Container is not null && !direct.Contains(id.RootId))
                        occurrence.AddGraphics(_transients, cancellationToken);

                    Report(progress, OperationStage.CreatingGraphics, index + 1, resolved.Count);
                }

                cancellationToken.ThrowIfCancellationRequested();
                transaction.Commit();
            }

            if (resolved.Count == 0)
            {
                RegenerateAllViewports(Application.DocumentManager.MdiActiveDocument!, progress, cancellationToken);
                return 0;
            }

            foreach (var target in direct)
            {
                cancellationToken.ThrowIfCancellationRequested();
                activeSpace.Remove(target);
            }

            _hidden = activeSpace;
            cancellationToken.ThrowIfCancellationRequested();
            Register();
            cancellationToken.ThrowIfCancellationRequested();
            ShowTransients(progress, cancellationToken);
            RegenerateAllViewports(Application.DocumentManager.MdiActiveDocument!, progress, cancellationToken);
            return resolved.Count;
        }
        catch
        {
            ClearAfterFailure(progress);
            throw;
        }
    }

    private static HashSet<ObjectId> ReadInventory(
        ObjectId[] inventory,
        CancellationToken cancellationToken,
        IProgress<OperationProgress>? progress)
    {
        var activeSpace = new HashSet<ObjectId>();
        progress?.Report(new OperationProgress(OperationStage.Isolating, 0, inventory.Length));

        for (var index = 0; index < inventory.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            activeSpace.Add(inventory[index]);
            Report(progress, OperationStage.Isolating, index + 1, inventory.Length);
        }

        return activeSpace;
    }

    private static void Report(
        IProgress<OperationProgress>? progress,
        OperationStage stage,
        int completed,
        int total)
    {
        if (completed % 64 == 0 || completed == total)
            progress?.Report(new OperationProgress(stage, completed, total));
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
    public void Clear(bool redraw = true, IProgress<OperationProgress>? progress = null)
    {
        progress?.Report(new OperationProgress(OperationStage.Clearing));
        System.Exception? failure = null;
        EntityIsolationService[] owners = [.. PendingRemoval.Where(owner => owner != this), this];

        foreach (var owner in owners)
        {
            try
            {
                owner.ClearOwnedGraphics(redraw && owner == this, progress);
            }
            catch (System.Exception exception)
            {
                failure ??= exception;
            }
        }

        if (failure is not null)
            throw new InvalidOperationException("Temporary isolation cleanup failed.", failure);
    }

    private void ClearOwnedGraphics(bool redraw, IProgress<OperationProgress>? progress)
    {
        var affectedDatabase = _database;
        _database = null;
        _hidden = [];
        var transients = _transients;
        _transients = [];
        System.Exception? failure = null;

        try
        {
            EraseTransients(progress);
        }
        catch (System.Exception exception)
        {
            failure = exception;
        }

        var disposed = 0;

        foreach (var transient in transients)
        {
            Report(progress, OperationStage.Clearing, ++disposed, transients.Count);
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
            RegenerateAllViewports(document, progress: progress);

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

    private void ShowTransients(
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var viewports = new IntegerCollection();
        var manager = TransientManager.CurrentTransientManager;
        progress?.Report(new OperationProgress(OperationStage.DisplayingGraphics, 0, _transients.Count));

        for (var index = 0; index < _transients.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transient = _transients[index];
            _displayed.Add(transient);

            if (!manager.AddTransient(transient, TransientDrawingMode.Main, TransientSubMode, viewports))
            {
                _displayed.Remove(transient);
                throw new InvalidOperationException("The nested object graphics could not be displayed.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, OperationStage.DisplayingGraphics, index + 1, _transients.Count);
        }
    }

    private void EraseTransients(IProgress<OperationProgress>? progress = null)
    {
        var viewports = new IntegerCollection();
        System.Exception? failure = null;

        Entity[] displayed = [.. _displayed];
        var manager = TransientManager.CurrentTransientManager;
        var completed = 0;

        foreach (var transient in displayed)
        {
            Report(progress, OperationStage.Clearing, ++completed, displayed.Length);
            try
            {
                if (!manager.EraseTransient(transient, viewports))
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

    private void ClearAfterFailure(IProgress<OperationProgress>? progress = null)
    {
        try
        {
            Clear(progress: progress);
        }
        catch (System.Exception)
        {
            // Cleanup attempts every resource; native redraw failures must not preserve a hiding rule.
        }
    }

    private static void RegenerateAllViewports(
        Document document,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(OperationStage.Regenerating));
        const int allViewports = 1;
        dynamic drawing = document.GetAcadDocument();
        cancellationToken.ThrowIfCancellationRequested();
        drawing.Regen(allViewports);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new OperationProgress(OperationStage.Regenerating, 1, 1));
    }
}