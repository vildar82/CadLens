using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;

namespace CadLens.Common.AutoCAD;

/// <summary>Reads the bounds of displayed geometry in an active transaction.</summary>
/// <remarks>
/// GeometricExtents of an insertion also covers members on frozen layers and hidden dynamic states,
/// and some custom objects report an empty box at the origin. Zoom Extents ignores both.
/// </remarks>
/// <param name="cancellationToken">Cooperative request cancellation.</param>
internal sealed class VisibleBounds(CancellationToken cancellationToken)
{
    private readonly Dictionary<ObjectId, Extents3d?> _definitions = [];
    private readonly HashSet<ObjectId> _reading = [];

    /// <summary>Returns the displayed bounds of an entity, or null when it shows nothing measurable.</summary>
    /// <param name="entity">Entity in its owner's coordinates.</param>
    internal Extents3d? Read(Entity entity)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (entity is not BlockReference block || block is MInsertBlock ||
            block.ExtensionDictionary.GetObject<DBDictionary>()?.Contains("ACAD_FILTER") == true ||
            block.BlockTableRecord.GetObject<BlockTableRecord>() is not
                {IsFromExternalReference: false, IsFromOverlayReference: false} definition)
            return ReadExtents(entity);

        return ReadInsertion(block, definition);
    }

    private Extents3d? ReadInsertion(BlockReference block, BlockTableRecord definition)
    {
        List<Extents3d> parts = [];

        if (ReadDefinition(definition) is { } members)
            parts.Add(members.TransformCorners(block.BlockTransform));

        // Attached attributes already use the insertion's owner coordinates.
        foreach (ObjectId id in block.AttributeCollection)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (id.GetObject<AttributeReference>() is {Invisible: false} attribute && IsDisplayed(attribute) &&
                ReadExtents(attribute) is { } extents)
                parts.Add(extents);
        }

        return Combine(parts);
    }

    private Extents3d? ReadDefinition(BlockTableRecord definition)
    {
        if (_definitions.TryGetValue(definition.ObjectId, out var known))
            return known;

        if (!_reading.Add(definition.ObjectId))
            return null;

        List<Extents3d> parts = [];

        foreach (ObjectId id in definition)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (id.GetObject<Entity>() is { } member && IsDisplayedMember(member) && Read(member) is { } extents)
                parts.Add(extents);
        }

        _reading.Remove(definition.ObjectId);
        var bounds = Combine(parts);
        _definitions.Add(definition.ObjectId, bounds);

        return bounds;
    }

    private static bool IsDisplayedMember(Entity member) =>
        IsDisplayed(member) && member is not (AttributeDefinition {Constant: false} or AttributeDefinition
        {
            Invisible: true
        });

    private static bool IsDisplayed(Entity entity)
    {
        var layer = entity.LayerId.GetObject<LayerTableRecord>();

        // Layer 0 members follow their insertion, which the caller already chose to include.
        return entity.Visible && (layer is null || layer.Name == "0" || layer is {IsOff: false, IsFrozen: false});
    }

    private static Extents3d? ReadExtents(Entity entity)
    {
        try
        {
            var extents = entity.GeometricExtents;

            return HasUsableBounds(extents) && (entity is DBPoint || extents.MinPoint != extents.MaxPoint)
                ? extents
                : null;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception exception) when (
            exception.ErrorStatus is ErrorStatus.NullExtents or ErrorStatus.InvalidExtents or ErrorStatus.NotApplicable)
        {
            // Bounds are optional for custom or empty entities; other native failures reach the queue.
            return null;
        }
    }

    private static Extents3d? Combine(List<Extents3d> parts)
    {
        Extents3d? bounds = null;

        foreach (var part in parts)
        {
            var combined = bounds ?? part;
            combined.AddExtents(part);
            bounds = combined;
        }

        return bounds;
    }

    private static bool HasUsableBounds(Extents3d bounds) =>
        bounds.MinPoint.X.IsFinite() && bounds.MinPoint.Y.IsFinite() && bounds.MinPoint.Z.IsFinite() &&
        bounds.MaxPoint.X.IsFinite() && bounds.MaxPoint.Y.IsFinite() && bounds.MaxPoint.Z.IsFinite() &&
        bounds.MinPoint.X <= bounds.MaxPoint.X && bounds.MinPoint.Y <= bounds.MaxPoint.Y &&
        bounds.MinPoint.Z <= bounds.MaxPoint.Z;
}