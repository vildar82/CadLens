using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadLens.Common.AutoCAD;

/// <summary>View navigation and selection without changes to drawing objects.</summary>
public static class EditorExtensions
{
    private const double Padding = 1.1;
    private const double Midpoint = 0.5;

    extension(Editor editor)
    {
        /// <summary>Fits world-space bounds in the current view, preserving its orientation and aspect ratio.</summary>
        /// <param name="extents">World-space bounds to display.</param>
        /// <param name="cancellationToken">Cooperative request cancellation.</param>
        public void Zoom(Extents3d extents, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var view = editor.GetCurrentView();
            cancellationToken.ThrowIfCancellationRequested();
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
            cancellationToken.ThrowIfCancellationRequested();
            editor.SetCurrentView(view);
            cancellationToken.ThrowIfCancellationRequested();
        }

        /// <summary>Selects direct current-space objects in the active drawing.</summary>
        /// <param name="objects">Candidate direct current-space objects.</param>
        /// <param name="cancellationToken">Cooperative request cancellation.</param>
        public int SelectObjects(IReadOnlyCollection<ObjectId> objects, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (objects.Count == 0)
            {
                editor.SetImpliedSelection([]);
                cancellationToken.ThrowIfCancellationRequested();
                editor.UpdateScreen();
                cancellationToken.ThrowIfCancellationRequested();
                return 0;
            }

            var database = Application.DocumentManager.MdiActiveDocument.Database;
            var selected = new List<ObjectId>();

            using (database.TransactionManager.StartTransaction())
            {
                foreach (var id in objects)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (id is {IsValid: true, IsErased: false} && id.Database == database &&
                        id.GetObject<Entity>()?.OwnerId == database.CurrentSpaceId)
                        selected.Add(id);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            editor.SetImpliedSelection([.. selected]);
            cancellationToken.ThrowIfCancellationRequested();
            editor.UpdateScreen();
            cancellationToken.ThrowIfCancellationRequested();
            return selected.Count;
        }
    }
}