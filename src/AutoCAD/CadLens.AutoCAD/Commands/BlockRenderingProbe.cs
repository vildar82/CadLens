using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;
using JetBrains.Annotations;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadLens.AutoCAD;

/// <summary>Tests per-insertion dimming of one simple block without editing the drawing.</summary>
[UsedImplicitly]
public sealed class BlockRenderingProbe
{
    private const string CommandName = "CADLENSBLOCKPROBE";
    private static readonly ProbeOverrule DrawingOverrule = new();
    private static readonly List<Entity> Parts = [];
    private static readonly List<Entity> ViewportParts = [];
    private static Document? _document;
    private static ObjectId _blockId;
    private static string? _drawError;
    private static bool _registered;

    /// <summary>Shows or clears the one-insertion rendering probe.</summary>
    [UsedImplicitly]
    [CommandMethod(CommandName, CommandFlags.NoUndoMarker)]
    public void Execute()
    {
        var document = Application.DocumentManager.MdiActiveDocument;

        if (document is null)
            return;

        if (_registered)
        {
            var error = _drawError;
            Clear();
            document.Editor.WriteMessage(error is null ? "\nBlock probe cleared." : $"\nBlock probe cleared. Draw error: {error}");
            return;
        }

        var prompt = new PromptEntityOptions("\nSelect a simple block insertion to test: ");
        prompt.SetRejectMessage("\nSelect a block insertion.");
        prompt.AddAllowedClass(typeof(BlockReference), true);
        var result = document.Editor.GetEntity(prompt);

        if (result.Status != PromptStatus.OK)
            return;

        try
        {
            Show(document, result.ObjectId);
            document.Editor.WriteMessage(_drawError is null
                ? $"\nBlock probe active for this insertion only: {Parts.Count} objects, {Parts.OfType<Hatch>().Count()} hatches. Run CADLENSBLOCKPROBE again to clear."
                : $"\nBlock probe draw failed: {_drawError}. Run CADLENSBLOCKPROBE again to clear.");
        }
        catch (System.Exception exception)
        {
            Clear(redraw: false);
            document.Editor.WriteMessage($"\nBlock probe unavailable: {exception.Message}");
        }
    }

    internal static void Clear(bool redraw = true)
    {
        Application.DocumentManager.DocumentToBeDeactivated -= OnContextLeaving;
        Application.DocumentManager.DocumentToBeDestroyed -= OnDocumentDestroyed;

        if (_registered)
        {
            Overrule.RemoveOverrule(RXObject.GetClass(typeof(BlockReference)), DrawingOverrule);
            _registered = false;
        }

        var document = _document;
        _document = null;
        _blockId = ObjectId.Null;
        _drawError = null;

        try
        {
            if (redraw && document == Application.DocumentManager.MdiActiveDocument)
                RegenerateAllViewports(document);
        }
        finally
        {
            foreach (var part in Parts)
                part.Dispose();

            Parts.Clear();
            ViewportParts.Clear();
        }
    }

    private static void Show(Document document, ObjectId blockId)
    {
        using (var transaction = document.Database.TransactionManager.StartTransaction())
        {
            var block = (BlockReference)transaction.GetObject(blockId, OpenMode.ForRead);
            var definition = (BlockTableRecord)transaction.GetObject(block.BlockTableRecord, OpenMode.ForRead);

            if (block.OwnerId != document.Database.CurrentSpaceId)
                throw new InvalidOperationException("Select a direct insertion in the active space.");

            if (definition.IsFromExternalReference || block.AttributeCollection.Count > 0)
                throw new InvalidOperationException("This probe does not support attributes or external references.");

            using var exploded = new DBObjectCollection();
            block.Explode(exploded);
            var visibleParts = new List<Entity>();
            var hiddenParts = new List<Entity>();

            try
            {
                foreach (DBObject item in exploded)
                {
                    if (item is not Entity entity || entity is BlockReference or AttributeDefinition)
                        throw new InvalidOperationException("This block contains nested or unsupported objects.");

                    if (!entity.Visible || IsLayerHidden(entity, transaction))
                    {
                        hiddenParts.Add(entity);
                        continue;
                    }

                    entity.Color = Color.FromRgb(65, 72, 80);

                    if (entity is Hatch hatch && hatch.BackgroundColor.ColorMethod != ColorMethod.None)
                        hatch.BackgroundColor = Color.FromRgb(40, 45, 50);

                    visibleParts.Add(entity);
                }
            }
            catch
            {
                foreach (DBObject item in exploded)
                    item.Dispose();

                throw;
            }

            foreach (var hiddenPart in hiddenParts)
                hiddenPart.Dispose();

            Parts.AddRange(visibleParts);
        }

        _document = document;
        _blockId = blockId;
        DrawingOverrule.SetCustomFilter();
        Overrule.AddOverrule(RXObject.GetClass(typeof(BlockReference)), DrawingOverrule, false);
        _registered = true;
        Overrule.Overruling = true;
        Application.DocumentManager.DocumentToBeDeactivated += OnContextLeaving;
        Application.DocumentManager.DocumentToBeDestroyed += OnDocumentDestroyed;
        RegenerateAllViewports(document);
    }

    private static bool IsLayerHidden(Entity entity, Transaction transaction)
    {
        var layer = (LayerTableRecord)transaction.GetObject(entity.LayerId, OpenMode.ForRead);
        return layer.IsOff || layer.IsFrozen;
    }

    private static void OnContextLeaving(object sender, DocumentCollectionEventArgs args)
    {
        if (args.Document == _document)
            Clear();
    }

    private static void OnDocumentDestroyed(object sender, DocumentCollectionEventArgs args)
    {
        if (args.Document == _document)
            Clear(redraw: false);
    }

    private static void RegenerateAllViewports(Document document)
    {
        const int allViewports = 1;
        dynamic drawing = document.GetAcadDocument();
        drawing.Regen(allViewports);
    }

    private sealed class ProbeOverrule : DrawableOverrule
    {
        public override bool IsApplicable(RXObject overruledSubject) =>
            overruledSubject is BlockReference block && block.ObjectId == _blockId && block.Database == _document?.Database;

        public override int SetAttributes(Drawable drawable, DrawableTraits traits)
        {
            var flags = base.SetAttributes(drawable, traits);

            if (traits is SubEntityTraits subTraits)
                subTraits.TrueColor = new EntityColor(65, 72, 80);

            return flags;
        }

        public override bool WorldDraw(Drawable drawable, WorldDraw worldDraw)
        {
            if (_drawError is not null)
                return base.WorldDraw(drawable, worldDraw);

            try
            {
                ViewportParts.Clear();

                foreach (var part in Parts)
                {
                    if (!part.WorldDraw(worldDraw))
                        ViewportParts.Add(part);
                }

                return ViewportParts.Count == 0;
            }
            catch (System.Exception exception)
            {
                _drawError = exception.Message;
                return base.WorldDraw(drawable, worldDraw);
            }
        }

        public override void ViewportDraw(Drawable drawable, ViewportDraw viewportDraw)
        {
            if (_drawError is not null)
            {
                base.ViewportDraw(drawable, viewportDraw);
                return;
            }

            try
            {
                foreach (var part in ViewportParts)
                    part.ViewportDraw(viewportDraw);
            }
            catch (System.Exception exception)
            {
                _drawError = exception.Message;
                base.ViewportDraw(drawable, viewportDraw);
            }
        }
    }
}
