using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;
using JetBrains.Annotations;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadLens.AutoCAD;

/// <summary>Tests temporary rendering of one hatch without changing the drawing.</summary>
[UsedImplicitly]
public sealed class HatchRenderingProbe
{
    private const string CommandName = "CADLENSHATCHPROBE";
    private static readonly ProbeOverrule DrawingOverrule = new();
    private static Document? _document;
    private static Hatch? _clone;
    private static ObjectId _hatchId;
    private static string? _drawError;
    private static bool _registered;

    /// <summary>Shows or clears the one-hatch rendering probe.</summary>
    [UsedImplicitly]
    [CommandMethod(CommandName, CommandFlags.NoUndoMarker)]
    public void Execute()
    {
        var document = Application.DocumentManager.MdiActiveDocument;

        if (document is null)
            return;

        if (_clone is not null)
        {
            var error = _drawError;
            Clear();
            document.Editor.WriteMessage(error is null ? "\nHatch probe cleared." : $"\nHatch probe cleared. Draw error: {error}");
            return;
        }

        var prompt = new PromptEntityOptions("\nSelect a hatch to test: ");
        prompt.SetRejectMessage("\nSelect a hatch.");
        prompt.AddAllowedClass(typeof(Hatch), true);
        var result = document.Editor.GetEntity(prompt);

        if (result.Status != PromptStatus.OK)
            return;

        try
        {
            Show(document, result.ObjectId);
            document.Editor.WriteMessage(_drawError is null
                ? "\nHatch probe active: dim gray pattern and background. Run CADLENSHATCHPROBE again to clear."
                : $"\nHatch probe draw failed: {_drawError}. Run CADLENSHATCHPROBE again to clear.");
        }
        catch (System.Exception exception)
        {
            Clear(redraw: false);
            document.Editor.WriteMessage($"\nHatch probe failed: {exception.Message}");
        }
    }

    internal static void Clear(bool redraw = true)
    {
        if (_clone is null)
            return;

        var document = _document;
        Application.DocumentManager.DocumentToBeDeactivated -= OnContextLeaving;
        Application.DocumentManager.DocumentToBeDestroyed -= OnDocumentDestroyed;

        if (_registered)
        {
            Overrule.RemoveOverrule(RXObject.GetClass(typeof(Hatch)), DrawingOverrule);
            _registered = false;
        }

        _document = null;
        _hatchId = ObjectId.Null;
        _drawError = null;

        try
        {
            if (redraw && document == Application.DocumentManager.MdiActiveDocument)
                RegenerateAllViewports(document);
        }
        finally
        {
            _clone.Dispose();
            _clone = null;
        }
    }

    private static void Show(Document document, ObjectId hatchId)
    {
        using (var transaction = document.Database.TransactionManager.StartTransaction())
        {
            var hatch = (Hatch)transaction.GetObject(hatchId, OpenMode.ForRead);

            if (hatch.OwnerId != document.Database.CurrentSpaceId)
                throw new InvalidOperationException("Select a direct hatch in the active space.");

            _clone = (Hatch)hatch.Clone();
            _clone.Color = Color.FromRgb(65, 72, 80);
            _clone.BackgroundColor = Color.FromRgb(40, 45, 50);
        }

        _document = document;
        _hatchId = hatchId;
        DrawingOverrule.SetCustomFilter();
        Overrule.AddOverrule(RXObject.GetClass(typeof(Hatch)), DrawingOverrule, false);
        _registered = true;
        Overrule.Overruling = true;
        Application.DocumentManager.DocumentToBeDeactivated += OnContextLeaving;
        Application.DocumentManager.DocumentToBeDestroyed += OnDocumentDestroyed;
        RegenerateAllViewports(document);
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
            overruledSubject is Hatch hatch && hatch.ObjectId == _hatchId && hatch.Database == _document?.Database;

        public override int SetAttributes(Drawable drawable, DrawableTraits traits)
        {
            var flags = base.SetAttributes(drawable, traits);

            if (traits is SubEntityTraits subTraits)
                subTraits.TrueColor = new EntityColor(65, 72, 80);

            return flags;
        }

        public override bool WorldDraw(Drawable drawable, WorldDraw worldDraw)
        {
            try
            {
                return _clone?.WorldDraw(worldDraw) ?? base.WorldDraw(drawable, worldDraw);
            }
            catch (System.Exception exception)
            {
                _drawError = exception.Message;
                return base.WorldDraw(drawable, worldDraw);
            }
        }

        public override void ViewportDraw(Drawable drawable, ViewportDraw viewportDraw)
        {
            if (_clone is null || _drawError is not null)
            {
                base.ViewportDraw(drawable, viewportDraw);
                return;
            }

            try
            {
                _clone.ViewportDraw(viewportDraw);
            }
            catch (System.Exception exception)
            {
                _drawError = exception.Message;
                base.ViewportDraw(drawable, viewportDraw);
            }
        }
    }
}
