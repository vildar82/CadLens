using System.Collections;
using System.Collections.Immutable;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using CadLens.Lenses;
using CadLens.Common;
using CadLens.Common.AutoCAD;
using JetBrains.Annotations;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

namespace CadLens.AutoCAD;

internal sealed class AutoCadEntitySnapshotReader(CancellationToken cancellationToken)
{
    private readonly Dictionary<ObjectId, DrawingNumberValue?> _blockEntityCounts = new();

    internal EntitySnapshot Read(Entity entity)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var properties = new Dictionary<DrawingPropertyKey, DrawingValue?>();
        var displayColor = ReadAppearance(entity, properties);
        ReadArea(entity, properties);
        var metric = ReadPrimitiveProperties(entity, properties);

        ReadBlockProperties(entity, properties);

        return new EntitySnapshot(
            new EntityId(entity.ObjectId),
            new LayerId(entity.LayerId),
            entity.GetRXClass().Name,
            properties.ToImmutableDictionary(),
            metric,
            displayColor);
    }

    private static int? ReadAppearance(Entity entity, Dictionary<DrawingPropertyKey, DrawingValue?> properties)
    {
        using var color = ReadOptional(() => entity.Color);
        properties[DrawingPropertyId.Color] = color is null
            ? null
            : ReadOptional(() => new DrawingColorValue(ReadColor(color)));
        properties[DrawingPropertyId.Linetype] = ReadLinetype(entity);
        properties[DrawingPropertyId.Lineweight] =
            ReadOptional(() => new DrawingLineweightValue(ReadLineweight(entity.LineWeight)));
        properties[DrawingPropertyId.LinetypeScale] = ReadNumber(() => entity.LinetypeScale, DrawingUnit.Scale);
        properties[DrawingPropertyId.Transparency] = ReadOptional(() => ReadTransparency(entity));

        return color is null ? null : ReadDisplayColor(color);
    }

    private static AssignedColor ReadColor(Color color)
    {
        if (color.IsByLayer)
            return new AssignedColor(AssignedColorKind.ByLayer);

        if (color.IsByBlock)
            return new AssignedColor(AssignedColorKind.ByBlock);

        if (color.IsByAci)
            return new AssignedColor(AssignedColorKind.Index, color.ColorIndex);

        if (color.HasBookName)
            return new AssignedColor(AssignedColorKind.ColorBook, ReadRgb(color), color.ColorName, color.BookName);

        return color.IsByColor
            ? new AssignedColor(AssignedColorKind.TrueColor, ReadRgb(color))
            : new AssignedColor(AssignedColorKind.Other, (int) color.ColorMethod, color.ColorMethod.ToString());
    }

    internal static int? ReadDisplayColor(Color color)
    {
        if (color.IsByLayer || color.IsByBlock)
            return null;

        if (color.IsByAci)
            return ReadScalar(() => EntityColor.LookUpRgb((byte) color.ColorIndex) & 0xFFFFFF);

        return color.IsByColor || color.HasBookName ? ReadScalar(() => ReadRgb(color)) : null;
    }

    private static int ReadRgb(Color color) => color.Red << 16 | color.Green << 8 | color.Blue;

    private static DrawingTextValue? ReadLinetype(Entity entity)
    {
        var name = ReadOptional(() => entity.Linetype);

        if (name is null)
            return null;

        if (string.Equals(name, "ByLayer", StringComparison.OrdinalIgnoreCase))
            return new DrawingTextValue("ByLayer", true);

        return string.Equals(name, "ByBlock", StringComparison.OrdinalIgnoreCase)
            ? new DrawingTextValue("ByBlock", true)
            : new DrawingTextValue(name);
    }

    private static AssignedLineweight ReadLineweight(LineWeight lineweight) => lineweight switch
    {
        LineWeight.ByLayer => new AssignedLineweight(AssignedLineweightKind.ByLayer),
        LineWeight.ByBlock => new AssignedLineweight(AssignedLineweightKind.ByBlock),
        LineWeight.ByLineWeightDefault => new AssignedLineweight(AssignedLineweightKind.Default),
        _ => new AssignedLineweight(AssignedLineweightKind.Explicit, (int) lineweight)
    };

    private static DrawingTransparencyValue? ReadTransparency(Entity entity)
    {
        var transparency = entity.Transparency;

        if (transparency.IsInvalid)
            return null;

        var value = transparency.IsByLayer
            ? new AssignedTransparency(AssignedTransparencyKind.ByLayer)
            : transparency.IsByBlock
                ? new AssignedTransparency(AssignedTransparencyKind.ByBlock)
                : new AssignedTransparency(AssignedTransparencyKind.Explicit, transparency.Alpha);

        return new DrawingTransparencyValue(value);
    }

    private static void ReadArea(Entity entity, Dictionary<DrawingPropertyKey, DrawingValue?> properties)
    {
        Func<double>? getArea = entity switch
        {
            Curve curve => () => curve.Area,
            Hatch hatch => () => hatch.Area,
            _ => null
        };

        if (getArea is not null)
            properties[DrawingPropertyId.Area] = ReadArea(getArea);
    }

    private static DrawingNumberValue? ReadArea(Func<double> getter)
    {
        try
        {
            return ReadNumber(getter, DrawingUnit.Area);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private DrawingPropertyId? ReadPrimitiveProperties(
        Entity entity,
        Dictionary<DrawingPropertyKey, DrawingValue?> properties) =>
        entity switch
        {
            Polyline polyline => ReadPolyline(polyline, properties),
            Polyline2d polyline => ReadPolyline(polyline, properties),
            Polyline3d polyline => ReadPolyline(polyline, properties),
            BlockReference block => ReadBlock(block, properties),
            Hatch hatch => ReadHatch(hatch, properties),
            Spline spline => ReadSpline(spline, properties),
            Mline mline => ReadMline(mline, properties),
            Line line => ReadLine(line, properties),
            Circle circle => ReadCircle(circle, properties),
            Arc arc => ReadArc(arc, properties),
            DBText text => ReadText(text, properties),
            MText text => ReadText(text, properties),
            _ => null
        };

    private static DrawingPropertyId ReadPolyline(
        Polyline polyline,
        Dictionary<DrawingPropertyKey, DrawingValue?> properties)
    {
        properties[DrawingPropertyId.Vertices] = ReadCount(() => polyline.NumberOfVertices);
        properties[DrawingPropertyId.Closed] = ReadBoolean(() => polyline.Closed);
        properties[DrawingPropertyId.Length] = ReadNumber(() => polyline.Length);
        properties[DrawingPropertyId.Width] = ReadConstantWidth(polyline);
        properties[DrawingPropertyId.Thickness] = ReadNumber(() => polyline.Thickness);

        return DrawingPropertyId.Vertices;
    }

    private static DrawingNumberValue? ReadConstantWidth(Polyline polyline)
    {
        var width = ReadNumber(() => polyline.ConstantWidth);

        if (width is {Value: 0} && ReadScalar(() => polyline.HasWidth) is not false)
            return null;

        return width;
    }

    private DrawingPropertyId ReadPolyline(Polyline2d polyline, Dictionary<DrawingPropertyKey, DrawingValue?> properties)
    {
        properties[DrawingPropertyId.Vertices] = ReadCount(() => CountLiveObjects<Vertex2d>(polyline));
        properties[DrawingPropertyId.Closed] = ReadBoolean(() => polyline.Closed);
        properties[DrawingPropertyId.Length] = ReadNumber(() => polyline.Length);
        properties[DrawingPropertyId.Width] = ReadNumber(() => polyline.ConstantWidth);
        properties[DrawingPropertyId.Thickness] = ReadNumber(() => polyline.Thickness);

        return DrawingPropertyId.Vertices;
    }

    private DrawingPropertyId ReadPolyline(Polyline3d polyline, Dictionary<DrawingPropertyKey, DrawingValue?> properties)
    {
        properties[DrawingPropertyId.Vertices] = ReadCount(() => CountLiveObjects<PolylineVertex3d>(polyline));
        properties[DrawingPropertyId.Closed] = ReadBoolean(() => polyline.Closed);
        properties[DrawingPropertyId.Length] = ReadNumber(() => polyline.Length);

        return DrawingPropertyId.Vertices;
    }

    private int CountLiveObjects<T>(IEnumerable objects) where T : Entity
    {
        var count = 0;

        foreach (ObjectId id in objects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (id.GetObject<T>() is not null)
                count++;
        }

        return count;
    }

    private DrawingPropertyId ReadBlock(BlockReference block, Dictionary<DrawingPropertyKey, DrawingValue?> properties)
    {
        var isDynamic = ReadScalar(() => block.IsDynamicBlock);
        properties[DrawingPropertyId.Dynamic] = isDynamic is { } dynamicBlock
            ? new DrawingBooleanValue(dynamicBlock)
            : null;
        properties[DrawingPropertyId.Attributes] = ReadCount(() => block.AttributeCollection.Count);
        properties[DrawingPropertyId.BlockName] = null;
        properties[DrawingPropertyId.DefinitionEntities] = null;
        properties[DrawingPropertyId.ExternalReference] = null;

        if (ReadScalar(() => block.BlockTableRecord) is not { } definitionId ||
            ReadOptional(() => definitionId.GetObject<BlockTableRecord>()) is not { } definition)
            return DrawingPropertyId.DefinitionEntities;

        var isExternal = ReadScalar(() => definition.IsFromExternalReference || definition.IsFromOverlayReference);
        properties[DrawingPropertyId.ExternalReference] = isExternal is { } external
            ? new DrawingBooleanValue(external)
            : null;
        properties[DrawingPropertyId.DefinitionEntities] = ReadBlockEntityCount(definition, isExternal);

        var nameDefinition = isDynamic is true &&
                             ReadScalar(() => block.DynamicBlockTableRecord) is { } originalId
            ? ReadOptional(() => originalId.GetObject<BlockTableRecord>()) ?? definition
            : definition;
        properties[DrawingPropertyId.BlockName] =
            ReadText(() => nameDefinition.Name) ?? ReadText(() => definition.Name);

        return DrawingPropertyId.DefinitionEntities;
    }

    private void ReadBlockProperties(Entity entity, Dictionary<DrawingPropertyKey, DrawingValue?> properties)
    {
        if (entity is not BlockReference block)
            return;

        ReadBlockAttributes(block, properties);
        ReadDynamicBlockProperties(block, properties);
    }

    private void ReadBlockAttributes(BlockReference block, Dictionary<DrawingPropertyKey, DrawingValue?> properties)
    {
        try
        {
            var attributes = new Dictionary<DrawingPropertyKey, DrawingValue?>();

            foreach (ObjectId id in block.AttributeCollection)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attribute = ReadOptional(() => id.GetObject<AttributeReference>());

                if (attribute is null)
                    continue;

                var tag = ReadOptional(() => attribute.Tag);

                if (tag is not {Length: > 0})
                    continue;

                var text = ReadOptional(() => ReadAttributeValue(attribute));
                AddNamedProperty(attributes, DrawingPropertyKey.ForAttribute(tag), text is null ? null : new DrawingTextValue(text));
            }

            foreach (var pair in attributes)
                properties.Add(pair.Key, pair.Value);
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            properties[DrawingPropertyId.Attributes] = null;
        }
    }

    private void ReadDynamicBlockProperties(BlockReference block, Dictionary<DrawingPropertyKey, DrawingValue?> properties)
    {
        try
        {
            if (!block.IsDynamicBlock)
                return;

            using var nativeProperties = block.DynamicBlockReferencePropertyCollection;
            var dynamicProperties = new Dictionary<DrawingPropertyKey, DrawingValue?>();

            foreach (DynamicBlockReferenceProperty property in nativeProperties)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = ReadDynamicProperty(() => property.PropertyName);

                if (name is not {Length: > 0})
                    continue;

                AddNamedProperty(dynamicProperties, DrawingPropertyKey.ForDynamicBlock(name), ReadDynamicProperty(() => ReadDynamicPropertyValue(property)));
            }

            cancellationToken.ThrowIfCancellationRequested();

            foreach (var pair in dynamicProperties)
                properties.Add(pair.Key, pair.Value);
        }
        catch (Exception)
        {
            // Native collection failures do not publish a partial set of dynamic properties.
        }
    }

    private static void AddNamedProperty(Dictionary<DrawingPropertyKey, DrawingValue?> properties, DrawingPropertyKey key, DrawingValue? value) =>
        properties[key] = properties.ContainsKey(key) ? null : value;

    private static T? ReadDynamicProperty<T>(Func<T?> getter) where T : class
    {
        try
        {
            return getter();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static DrawingValue? ReadDynamicPropertyValue(DynamicBlockReferenceProperty property) => property.Value switch
    {
        string text => new DrawingTextValue(text),
        bool value => new DrawingBooleanValue(value),
        double value => ReadDynamicNumber(value, property.UnitsType),
        float value => ReadDynamicNumber(value, property.UnitsType),
        short value => ReadDynamicNumber(value, property.UnitsType),
        int value => ReadDynamicNumber(value, property.UnitsType),
        _ => null
    };

    private static DrawingNumberValue? ReadDynamicNumber(double value, DynamicBlockReferencePropertyUnitsType units)
    {
        DrawingUnit? unit = units switch
        {
            DynamicBlockReferencePropertyUnitsType.NoUnits => DrawingUnit.Scale,
            DynamicBlockReferencePropertyUnitsType.Angular => DrawingUnit.Angle,
            DynamicBlockReferencePropertyUnitsType.Distance => DrawingUnit.Distance,
            DynamicBlockReferencePropertyUnitsType.Area => DrawingUnit.Area,
            _ => null
        };

        return unit is { } knownUnit && value.IsFinite()
            ? new DrawingNumberValue(value == 0 ? 0 : value, knownUnit)
            : null;
    }

    private static string ReadAttributeValue(AttributeReference attribute)
    {
        if (!attribute.IsMTextAttribute)
            return attribute.TextString;

        using var text = attribute.MTextAttribute;
        return text.Text;
    }

    private DrawingNumberValue? ReadBlockEntityCount(BlockTableRecord definition, bool? isExternal)
    {
        if (_blockEntityCounts.TryGetValue(definition.ObjectId, out var count))
            return count;

        count = isExternal is false ? ReadCount(() => CountLiveObjects<Entity>(definition)) : null;
        _blockEntityCounts.Add(definition.ObjectId, count);

        return count;
    }

    private static DrawingPropertyId ReadHatch(Hatch hatch, Dictionary<DrawingPropertyKey, DrawingValue?> properties)
    {
        properties[DrawingPropertyId.BoundaryLoops] = ReadCount(() => hatch.NumberOfLoops);
        var isGradient = ReadScalar(() => hatch.IsGradient);
        var isSolid = ReadScalar(() => hatch.IsSolidFill);
        var fillKind = (isGradient, isSolid) switch
        {
            (true, _) => "Gradient",
            (false, true) => "Solid",
            (false, false) => "Pattern",
            _ => null
        };
        properties[DrawingPropertyId.FillKind] = fillKind is null ? null : new DrawingTextValue(fillKind, true);

        switch (isGradient, isSolid)
        {
            case (true, _):
                properties[DrawingPropertyId.Gradient] = ReadText(() => hatch.GradientName);
                break;
            case (false, false):
                properties[DrawingPropertyId.Pattern] = ReadText(() => hatch.PatternName);
                properties[DrawingPropertyId.PatternType] = ReadText(() => hatch.PatternType.ToString(), true);
                properties[DrawingPropertyId.PatternAngle] = ReadNumber(() => hatch.PatternAngle, DrawingUnit.Angle);
                properties[DrawingPropertyId.PatternScale] = ReadNumber(() => hatch.PatternScale, DrawingUnit.Scale);
                break;
        }

        return DrawingPropertyId.BoundaryLoops;
    }

    private static DrawingPropertyId ReadSpline(Spline spline, Dictionary<DrawingPropertyKey, DrawingValue?> properties)
    {
        properties[DrawingPropertyId.ControlPoints] = ReadCount(() => spline.NumControlPoints);
        properties[DrawingPropertyId.FitPoints] = ReadCount(() => spline.NumFitPoints);
        properties[DrawingPropertyId.Closed] = ReadBoolean(() => spline.Closed);
        properties[DrawingPropertyId.Length] = ReadNumber(() => spline.GetDistanceAtParameter(spline.EndParam));

        return DrawingPropertyId.ControlPoints;
    }

    private static DrawingPropertyId ReadMline(Mline mline, Dictionary<DrawingPropertyKey, DrawingValue?> properties)
    {
        properties[DrawingPropertyId.Vertices] = ReadCount(() => mline.NumberOfVertices);

        return DrawingPropertyId.Vertices;
    }

    private static DrawingPropertyId ReadLine(Line line, Dictionary<DrawingPropertyKey, DrawingValue?> properties)
    {
        properties[DrawingPropertyId.Length] = ReadNumber(() => line.Length);
        properties[DrawingPropertyId.Thickness] = ReadNumber(() => line.Thickness);

        return DrawingPropertyId.Length;
    }

    private static DrawingPropertyId ReadCircle(Circle circle, Dictionary<DrawingPropertyKey, DrawingValue?> properties)
    {
        properties[DrawingPropertyId.Radius] = ReadNumber(() => circle.Radius);
        properties[DrawingPropertyId.Length] = ReadNumber(() => circle.Circumference);
        properties[DrawingPropertyId.Thickness] = ReadNumber(() => circle.Thickness);

        return DrawingPropertyId.Radius;
    }

    private static DrawingPropertyId ReadArc(Arc arc, Dictionary<DrawingPropertyKey, DrawingValue?> properties)
    {
        properties[DrawingPropertyId.Radius] = ReadNumber(() => arc.Radius);
        properties[DrawingPropertyId.Length] = ReadNumber(() => arc.Length);
        properties[DrawingPropertyId.StartAngle] = ReadNumber(() => arc.StartAngle, DrawingUnit.Angle);
        properties[DrawingPropertyId.EndAngle] = ReadNumber(() => arc.EndAngle, DrawingUnit.Angle);
        properties[DrawingPropertyId.Thickness] = ReadNumber(() => arc.Thickness);

        return DrawingPropertyId.Radius;
    }

    private static DrawingPropertyId ReadText(DBText text, Dictionary<DrawingPropertyKey, DrawingValue?> properties)
    {
        properties[DrawingPropertyId.TextHeight] = ReadNumber(() => text.Height);
        properties[DrawingPropertyId.Text] = ReadText(() => text.TextString);
        properties[DrawingPropertyId.TextStyle] = ReadText(() => text.TextStyleName);

        return DrawingPropertyId.TextHeight;
    }

    private static DrawingPropertyId ReadText(MText text, Dictionary<DrawingPropertyKey, DrawingValue?> properties)
    {
        properties[DrawingPropertyId.TextHeight] = ReadNumber(() => text.TextHeight);
        properties[DrawingPropertyId.Text] = ReadText(() => text.Text);
        properties[DrawingPropertyId.TextStyle] = ReadText(() => text.TextStyleName);

        return DrawingPropertyId.TextHeight;
    }

    private static DrawingNumberValue? ReadNumber(Func<double> getter, DrawingUnit unit = DrawingUnit.Distance) =>
        ReadScalar(getter) is { } value && value.IsFinite()
            ? new DrawingNumberValue(value == 0 ? 0 : value, unit)
            : null;

    private static DrawingNumberValue? ReadCount(Func<int> getter) =>
        ReadScalar(getter) is >= 0 and var value ? new DrawingNumberValue(value, DrawingUnit.Count) : null;

    private static DrawingBooleanValue? ReadBoolean(Func<bool> getter) =>
        ReadScalar(getter) is { } value ? new DrawingBooleanValue(value) : null;

    private static DrawingTextValue? ReadText(Func<string> getter, bool isApplicationText = false) =>
        ReadOptional(getter) is { } text ? new DrawingTextValue(text, isApplicationText) : null;

    private static T? ReadScalar<T>(Func<T> getter) where T : struct
    {
        try
        {
            return getter();
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            return null;
        }
    }

    private static T? ReadOptional<T>([InstantHandle] Func<T?> getter) where T : class
    {
        try
        {
            return getter();
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            return null;
        }
    }

    private static bool IsUnavailable(Exception exception) => exception.ErrorStatus is
        ErrorStatus.NotApplicable or ErrorStatus.NotImplementedYet or ErrorStatus.InvalidInput or
        ErrorStatus.DegenerateGeometry or ErrorStatus.NullExtents or ErrorStatus.InvalidExtents;
}