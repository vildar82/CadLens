using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.GraphicsInterface;
using CadLens.Common.AutoCAD;
using Xunit;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadLens.AutoCAD;

/// <summary>Checks restoration ownership on the production isolation implementation.</summary>
[Collection("AutoCAD")]
public sealed class EntityIsolationServiceTests
{
    /// <summary>Suspension shows every candidate and restores the exact original hidden set only once.</summary>
    [Fact]
    public void SuspensionRestoresExactSetOnce()
    {
        var document = CreateDocument();
        var graphics = new EntityIsolationService();
        var visible = document.Database.Add(new Entity());
        var hidden = document.Database.Add(new Entity());
        graphics.Apply(document.Database, [visible], [visible, hidden]);

        try
        {
            var restore = graphics.Suspend();
            Assert.False(graphics.IsApplicable(document.Database.Objects[hidden.Value]));
            Assert.Contains(graphics, DrawableOverrule.Registered);
            restore();
            var redraws = document.Drawing.RegenCount;
            restore();

            Assert.Equal(redraws, document.Drawing.RegenCount);
            Assert.False(graphics.IsApplicable(document.Database.Objects[visible.Value]));
            Assert.True(graphics.IsApplicable(document.Database.Objects[hidden.Value]));
        }
        finally
        {
            graphics.Clear(false);
        }
    }

    /// <summary>Clear during suspension, including a reentrant reset during redraw, invalidates restoration.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClearInvalidatesSuspendedEffects(bool duringRedraw)
    {
        var document = CreateDocument();
        var graphics = new EntityIsolationService();
        var hidden = document.Database.Add(new Entity());
        graphics.Apply(document.Database, [], [hidden]);

        if (duringRedraw)
            document.Drawing.OnRegen = () => graphics.Clear(false);

        var restore = graphics.Suspend();

        if (!duringRedraw)
            graphics.Clear(false);

        restore();
        Assert.False(graphics.IsApplicable(document.Database.Objects[hidden.Value]));
        Assert.DoesNotContain(graphics, DrawableOverrule.Registered);
    }

    /// <summary>Switching drawing or space cannot restore effects captured in the previous context.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangedContextDoesNotRestoreEffects(bool changeDocument)
    {
        var document = CreateDocument();
        var graphics = new EntityIsolationService();
        var hidden = document.Database.Add(new Entity());
        graphics.Apply(document.Database, [], [hidden]);

        try
        {
            var restore = graphics.Suspend();

            if (changeDocument)
                Application.DocumentManager.MdiActiveDocument = new Document();
            else
                document.Database.CurrentSpaceId = document.Database.Add(new BlockTableRecord());

            restore();
            Assert.False(graphics.IsApplicable(document.Database.Objects[hidden.Value]));
        }
        finally
        {
            graphics.Clear(false);
        }
    }

    /// <summary>A failed suspension redraw restores the previous hidden set before reporting the error.</summary>
    [Fact]
    public void FailedRedrawKeepsPreviousHiddenSet()
    {
        var document = CreateDocument();
        var graphics = new EntityIsolationService();
        var hidden = document.Database.Add(new Entity());
        graphics.Apply(document.Database, [], [hidden]);
        document.Drawing.OnRegen = () => throw new InvalidOperationException("redraw fixture failure");

        try
        {
            Assert.Throws<InvalidOperationException>(graphics.Suspend);
            Assert.True(graphics.IsApplicable(document.Database.Objects[hidden.Value]));
            Assert.Contains(graphics, DrawableOverrule.Registered);
        }
        finally
        {
            graphics.Clear(false);
        }
    }

    private static Document CreateDocument()
    {
        Application.DocumentManager = new DocumentCollection();
        var document = Application.DocumentManager.MdiActiveDocument!;
        document.Database.CurrentSpaceId = document.Database.Add(new BlockTableRecord());
        return document;
    }
}
