using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.PlottingServices;
using CadLens.Common.AutoCAD;
using Xunit;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadLens.AutoCAD;

/// <summary>Checks what the production paint overrule draws and when it stops.</summary>
[Collection("AutoCAD")]
public sealed class ObjectPaintServiceTests
{
    private const int Rgb = 0x102030;

    /// <summary>Only listed objects are painted, with the RGB value they were given.</summary>
    [Fact]
    public void PaintsListedObjectsOnly()
    {
        var document = Application.DocumentManager.MdiActiveDocument!;
        var listed = new Entity();
        var other = new Entity();
        var paint = new ObjectPaintService();
        paint.Apply(document, new Dictionary<ObjectId, int> {[document.Database.Add(listed)] = Rgb});

        try
        {
            var color = Draw(paint, listed);

            Assert.Equal((0x10, 0x20, 0x30), (color!.Value.Red, color.Value.Green, color.Value.Blue));
            Assert.Null(Draw(paint, other));
        }
        finally
        {
            paint.Clear(false);
        }
    }

    /// <summary>Objects drawn inside a painted object take its color, as block members must.</summary>
    [Fact]
    public void NestedObjectsFollowPaintedContainer()
    {
        var document = Application.DocumentManager.MdiActiveDocument!;
        var container = new Entity();
        var member = new Entity();
        var paint = new ObjectPaintService();
        paint.Apply(document, new Dictionary<ObjectId, int> {[document.Database.Add(container)] = Rgb});
        var traits = new DrawableTraits();
        paint.NestedDrawing = () => paint.SetAttributes(member, traits);

        try
        {
            paint.WorldDraw(container, new WorldDraw());
            Assert.NotNull(traits.TrueColor);

            traits.TrueColor = null;
            paint.NestedDrawing();
            Assert.Null(traits.TrueColor);
        }
        finally
        {
            paint.Clear(false);
        }
    }

    /// <summary>A plot keeps the drawing's colors, and the screen paint returns after it.</summary>
    [Fact]
    public void PlotSuspendsPaint()
    {
        var document = Application.DocumentManager.MdiActiveDocument!;
        var listed = new Entity();
        var paint = new ObjectPaintService();
        var plots = PlotReactorManager.Latest!;
        paint.Apply(document, new Dictionary<ObjectId, int> {[document.Database.Add(listed)] = Rgb});

        try
        {
            plots.RaiseBeginPlot();
            Assert.Null(Draw(paint, listed));

            plots.RaiseEndPlot();
            Assert.NotNull(Draw(paint, listed));
        }
        finally
        {
            paint.Clear(false);
        }
    }

    /// <summary>Clearing removes the overrule, releases the plot watcher, and redraws the drawing.</summary>
    [Fact]
    public void ClearRemovesPaintAndRedraws()
    {
        var document = Application.DocumentManager.MdiActiveDocument!;
        var paint = new ObjectPaintService();
        var plots = PlotReactorManager.Latest!;
        paint.Apply(document, new Dictionary<ObjectId, int> {[document.Database.Add(new Entity())] = Rgb});
        var redraws = document.Drawing.RegenCount;

        paint.Clear();

        Assert.DoesNotContain(paint, DrawableOverrule.Registered);
        Assert.False(plots.IsWatched);
        Assert.Equal(redraws + 1, document.Drawing.RegenCount);
    }

    private static Autodesk.AutoCAD.Colors.EntityColor? Draw(ObjectPaintService paint, Entity entity)
    {
        var traits = new DrawableTraits();
        paint.SetAttributes(entity, traits);

        return traits.TrueColor;
    }
}