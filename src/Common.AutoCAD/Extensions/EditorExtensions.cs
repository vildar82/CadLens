using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace Common.AutoCAD;

/// <summary>View navigation and selection without changes to drawing objects.</summary>
public static class EditorExtensions
{
    private const double Padding = 1.1;
    private const double Midpoint = 0.5;

    /// <summary>Fits world-space bounds in the current view, preserving its orientation and aspect ratio.</summary>
    /// <param name="editor">Editor owning the active view.</param>
    /// <param name="extents">World-space bounds to display.</param>
    public static void Zoom(this Editor editor, Extents3d extents)
    {
        using var view = editor.GetCurrentView();
        var eyeToWorld = Matrix3d.Rotation(-view.ViewTwist, view.ViewDirection, view.Target) *
                         Matrix3d.Displacement(view.Target - Point3d.Origin) *
                         Matrix3d.PlaneToWorld(view.ViewDirection);
        extents.TransformBy(eyeToWorld.Inverse());
        var width = extents.MaxPoint.X - extents.MinPoint.X;
        var height = extents.MaxPoint.Y - extents.MinPoint.Y;
        var aspect = view.Width / view.Height;

        // Center point-sized targets without changing their current zoom scale.
        if (width > 0 || height > 0)
        {
            view.Height = Math.Max(height, width / aspect) * Padding;
            view.Width = view.Height * aspect;
        }

        view.CenterPoint = new Point2d(
            extents.MinPoint.X * Midpoint + extents.MaxPoint.X * Midpoint,
            extents.MinPoint.Y * Midpoint + extents.MaxPoint.Y * Midpoint);
        editor.SetCurrentView(view);
    }

    /// <summary>Selects direct current-space objects in the active drawing.</summary>
    public static int SelectObjects(this Editor editor, IReadOnlyCollection<ObjectId> objects)
    {
        if (objects.Count == 0)
        {
            editor.SetImpliedSelection([]);
            return 0;
        }

        var database = Application.DocumentManager.MdiActiveDocument.Database;
        var selected = new List<ObjectId>();

        using (var transaction = database.TransactionManager.StartTransaction())
        {
            foreach (var id in objects)
            {
                if (!id.IsValid || id.IsErased || id.Database != database)
                    continue;

                var entity = id.GetObject<Entity>();

                if (entity?.OwnerId == database.CurrentSpaceId)
                    selected.Add(id);
            }
        }

        editor.SetImpliedSelection([.. selected]);
        return selected.Count;
    }
}
