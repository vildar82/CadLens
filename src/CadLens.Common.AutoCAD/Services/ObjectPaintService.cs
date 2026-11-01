using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.PlottingServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadLens.Common.AutoCAD;

/// <summary>Temporarily redraws objects in given colors without changing drawing properties.</summary>
public sealed class ObjectPaintService : DrawableOverrule
{
    private readonly PlotReactorManager _plots = new();
    private Dictionary<ObjectId, EntityColor> _colors = new();
    private Database? _database;
    private EntityColor? _containerColor;
    private bool _plotting;
    private bool _registered;

    /// <summary>Replaces the painted objects and regenerates the drawing.</summary>
    /// <param name="document">Active drawing that owns the objects.</param>
    /// <param name="colors">Direct active-space objects and their RGB colors.</param>
    /// <param name="progress">Optional operation step.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    internal void Apply(
        Document document,
        IReadOnlyDictionary<ObjectId, int> colors,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _database = document.Database;
        _colors = colors.ToDictionary(pair => pair.Key, pair => ToEntityColor(pair.Value));

        try
        {
            Register();
            document.RegenerateAllViewports(progress, cancellationToken);
        }
        catch
        {
            ClearAfterFailure();
            throw;
        }
    }

    /// <summary>Removes the paint.</summary>
    /// <param name="redraw">False when shutdown forbids regeneration.</param>
    /// <param name="progress">Optional operation step.</param>
    public void Clear(bool redraw = true, IProgress<OperationProgress>? progress = null)
    {
        var affectedDatabase = _database;
        _database = null;
        _colors = new Dictionary<ObjectId, EntityColor>();
        _plotting = false;

        if (_registered)
        {
            _plots.BeginPlot -= OnBeginPlot;
            _plots.EndPlot -= OnEndPlot;
            RemoveOverrule(GetClass(typeof(Entity)), this);
            _registered = false;
        }

        var document = Application.DocumentManager.MdiActiveDocument;

        if (redraw && document is not null && document.Database == affectedDatabase)
            document.RegenerateAllViewports(progress);
    }

    /// <inheritdoc />
    public override int SetAttributes(Drawable drawable, DrawableTraits traits)
    {
        var flags = base.SetAttributes(drawable, traits);

        if (!_plotting && (_containerColor ?? FindColor(drawable)) is { } color)
            traits.TrueColor = color;

        return flags;
    }

    /// <inheritdoc />
    public override bool WorldDraw(Drawable drawable, WorldDraw wd)
    {
        var outerColor = _containerColor;
        // Block members, attributes, and dimension parts draw inside this call and follow their container.
        _containerColor ??= FindColor(drawable);

        try
        {
            return base.WorldDraw(drawable, wd);
        }
        finally
        {
            _containerColor = outerColor;
        }
    }

    private EntityColor? FindColor(Drawable drawable) =>
        drawable is Entity entity && _colors.TryGetValue(entity.ObjectId, out var color) ? color : null;

    private static EntityColor ToEntityColor(int rgb) =>
        new((byte) (rgb >> 16), (byte) (rgb >> 8), (byte) rgb);

    private void Register()
    {
        if (_registered)
            return;

        AddOverrule(GetClass(typeof(Entity)), this, false);
        _registered = true;
        Overruling = true;
        _plots.BeginPlot += OnBeginPlot;
        _plots.EndPlot += OnEndPlot;
    }

    // Paint is a screen aid; plots must keep the drawing's own colors.
    private void OnBeginPlot(object sender, BeginPlotEventArgs args) => _plotting = true;

    private void OnEndPlot(object sender, EndPlotEventArgs args) => _plotting = false;

    private void ClearAfterFailure()
    {
        try
        {
            Clear();
        }
        catch (System.Exception)
        {
            // The original failure explains the request; a native cleanup failure must not replace it.
        }
    }
}