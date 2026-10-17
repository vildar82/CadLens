using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadLens.Common.AutoCAD;

internal sealed class EntityOccurrence
{
    private readonly List<(Entity Entity, ObjectId Layer)> _path = [];

    internal Entity Source => _path[^1].Entity;
    internal Matrix3d Transform { get; private set; } = Matrix3d.Identity;
    internal bool IsVisible { get; private set; } = true;

    internal static EntityOccurrence? Resolve(Database database, EntityId target)
    {
        var ids = new List<EntityId>();

        for (var node = target; node is not null; node = node.Container)
            ids.Add(node);

        ids.Reverse();
        var result = new EntityOccurrence();
        var owner = database.CurrentSpaceId;
        var inheritedLayer = default(ObjectId);
        var parentTransform = Matrix3d.Identity;
        BlockReference? parent = null;
        HashSet<ObjectId> frozen = [];
        var document = Application.DocumentManager.MdiActiveDocument;

        if (document?.Database == database &&
            document.Editor.CurrentViewportObjectId.GetObject<Viewport>() is { } viewport)
            frozen = [.. viewport.GetFrozenLayers().Cast<ObjectId>()];

        foreach (var id in ids)
        {
            if (id.NativeId.Database != database || id.NativeId.GetObject<Entity>() is not { } entity)
                return null;

            if (entity is AttributeReference && parent is not null && entity.OwnerId == parent.ObjectId)
                result.Transform = parentTransform;
            else if (entity.OwnerId != owner)
                return null;

            var layer = entity.LayerId.GetObject<LayerTableRecord>();
            var layerId = layer?.Name == "0" && inheritedLayer.IsValid ? inheritedLayer : entity.LayerId;
            var effectiveLayer = layerId.GetObject<LayerTableRecord>();
            result._path.Add((entity, layerId));
            result.IsVisible &= entity.Visible && effectiveLayer is {IsOff: false, IsFrozen: false} &&
                                !frozen.Contains(layerId) &&
                                entity is not (AttributeReference {Invisible: true} or AttributeDefinition
                                {
                                    Invisible: true
                                });

            if (id == target)
                continue;

            if (entity is not BlockReference block || block is MInsertBlock)
                return null;

            var definition = block.BlockTableRecord.GetObject<BlockTableRecord>();

            if (definition is null || definition.IsFromExternalReference || definition.IsFromOverlayReference)
                return null;

            parent = block;
            parentTransform = result.Transform;
            result.Transform *= block.BlockTransform;
            inheritedLayer = layerId;
            owner = definition.ObjectId;
        }

        return result;
    }

    internal void AddGraphics(List<Entity> graphics)
    {
        if (!IsVisible)
            return;

        foreach (var (entity, _) in _path)
        {
            if (entity is BlockReference &&
                entity.ExtensionDictionary.GetObject<DBDictionary>()?.Contains("ACAD_FILTER") == true)
                throw new NotSupportedException("Isolation inside clipped blocks is unavailable.");
        }

        AddGraphics(Source, graphics, canExplode: true);
    }

    internal Extents3d? ReadBounds()
    {
        try
        {
            var bounds = Source.GeometricExtents;
            bounds.TransformBy(Transform);

            return bounds.MinPoint.X.IsFinite() && bounds.MinPoint.Y.IsFinite() && bounds.MinPoint.Z.IsFinite() &&
                   bounds.MaxPoint.X.IsFinite() && bounds.MaxPoint.Y.IsFinite() && bounds.MaxPoint.Z.IsFinite() &&
                   bounds.MinPoint.X <= bounds.MaxPoint.X && bounds.MinPoint.Y <= bounds.MaxPoint.Y &&
                   bounds.MinPoint.Z <= bounds.MaxPoint.Z
                ? bounds
                : null;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception exception) when (
            exception.ErrorStatus is ErrorStatus.NullExtents or ErrorStatus.InvalidExtents or
                ErrorStatus.NotApplicable)
        {
            return null;
        }
    }

    private void AddGraphics(Entity source, List<Entity> graphics, bool canExplode)
    {
        // Complex entities can retain database-owned children in a shallow transformed copy.
        // Explode those entities into independent in-memory geometry before transforming anything.
        if (source.GetType() == typeof(Polyline2d) || source.GetType() == typeof(Polyline3d))
        {
            if (!canExplode)
                throw new NotSupportedException("The nested object has unsupported graphics.");

            using var parts = new DBObjectCollection();

            try
            {
                source.Explode(parts);

                foreach (var part in parts.Cast<DBObject>())
                {
                    if (part is not Entity entity)
                        throw new NotSupportedException("The nested object has unsupported graphics.");

                    AddGraphics(entity, graphics, canExplode: false);
                }

                if (parts.Count == 0)
                    throw new NotSupportedException("The nested object has no supported graphics.");
            }
            finally
            {
                foreach (var part in parts.Cast<DBObject>())
                    part.Dispose();
            }

            return;
        }

        var type = source.GetType();

        if (type != typeof(Line) && type != typeof(Circle) && type != typeof(Arc) && type != typeof(Ellipse) &&
            type != typeof(Polyline) && type != typeof(Spline) && type != typeof(DBText) && type != typeof(MText) &&
            type != typeof(AttributeDefinition) && type != typeof(AttributeReference) && type != typeof(Hatch))
            throw new NotSupportedException("Isolation inside blocks does not support this object type.");

        var clone = source.GetTransformedCopy(Transform);
        graphics.Add(clone);
        ApplyAppearance(clone);
    }

    private void ApplyAppearance(Entity clone)
    {
        using var defaultColor = Color.FromColorIndex(ColorMethod.ByAci, 7);
        clone.Color = defaultColor;
        clone.LinetypeId = Source.Database.ContinuousLinetype;
        clone.LineWeight = LineWeight.ByLineWeightDefault;
        clone.Transparency = new Transparency(byte.MaxValue);

        foreach (var (entity, layerId) in _path)
        {
            var layer = layerId.GetObject<LayerTableRecord>()!;
            using var color = entity.Color;

            if (!color.IsByBlock)
            {
                using var layerColor = layer.Color;
                clone.Color = color.IsByLayer ? layerColor : color;
            }

            if (!string.Equals(entity.Linetype, "ByBlock", StringComparison.OrdinalIgnoreCase))
                clone.LinetypeId = string.Equals(entity.Linetype, "ByLayer", StringComparison.OrdinalIgnoreCase)
                    ? layer.LinetypeObjectId
                    : entity.LinetypeId;

            if (entity.LineWeight != LineWeight.ByBlock)
                clone.LineWeight = entity.LineWeight == LineWeight.ByLayer ? layer.LineWeight : entity.LineWeight;

            if (!entity.Transparency.IsByBlock)
                clone.Transparency = entity.Transparency.IsByLayer ? layer.Transparency : entity.Transparency;

            clone.LayerId = layerId;
        }
    }
}