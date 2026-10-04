using System.Collections.Immutable;
using Common;

namespace CadLens.Lenses;

/// <summary>Reads the same observed properties for details, grouping, and row measurements.</summary>
public static class DrawingProperties
{
    /// <summary>Returns observed grouping choices without maintaining a primitive type catalog.</summary>
    /// <param name="entities">Entities in the current type scope.</param>
    public static ImmutableArray<DrawingPropertyId> GetAvailableFields(IEnumerable<EntitySnapshot> entities) =>
    [
        .. entities.SelectMany(entity => entity.Properties?.Keys ?? [])
            .Concat([DrawingPropertyId.Layer])
            .Distinct()
            .OrderBy(id => id)
    ];

    /// <summary>Returns the English application label for one known property.</summary>
    /// <param name="id">Property identity.</param>
    public static string GetLabel(DrawingPropertyId id) => id switch
    {
        DrawingPropertyId.Layer => "Layer",
        DrawingPropertyId.Color => "Color",
        DrawingPropertyId.Linetype => "Linetype",
        DrawingPropertyId.Lineweight => "Lineweight",
        DrawingPropertyId.LinetypeScale => "Linetype scale",
        DrawingPropertyId.Transparency => "Transparency",
        DrawingPropertyId.Vertices => "Vertices",
        DrawingPropertyId.Closed => "Closed",
        DrawingPropertyId.Length => "Length",
        DrawingPropertyId.Width => "Constant width",
        DrawingPropertyId.Thickness => "Extrusion thickness",
        DrawingPropertyId.DefinitionEntities => "Definition entities",
        DrawingPropertyId.BlockName => "Block name",
        DrawingPropertyId.Attributes => "Attributes",
        DrawingPropertyId.Dynamic => "Dynamic",
        DrawingPropertyId.ExternalReference => "External reference",
        DrawingPropertyId.BoundaryLoops => "Boundary loops",
        DrawingPropertyId.FillKind => "Fill kind",
        DrawingPropertyId.Pattern => "Pattern",
        DrawingPropertyId.PatternAngle => "Pattern angle",
        DrawingPropertyId.PatternScale => "Pattern scale",
        DrawingPropertyId.PatternType => "Pattern type",
        DrawingPropertyId.Gradient => "Gradient",
        DrawingPropertyId.ControlPoints => "Control points",
        DrawingPropertyId.FitPoints => "Fit points",
        DrawingPropertyId.Radius => "Radius",
        DrawingPropertyId.StartAngle => "Start angle",
        DrawingPropertyId.EndAngle => "End angle",
        DrawingPropertyId.TextHeight => "Text height",
        DrawingPropertyId.Text => "Text content",
        DrawingPropertyId.TextStyle => "Text style",
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown drawing property.")
    };

    /// <summary>Obtains the declared row measurement without affecting the placed target count.</summary>
    /// <param name="entity">Detached entity facts.</param>
    public static DrawingMetric? GetPrimaryMetric(EntitySnapshot entity) =>
        entity.PrimaryMetric is { } id ? new DrawingMetric(id, ReadValue(entity, id) as DrawingNumberValue) : null;

    /// <summary>Reads a property, using assigned layer metadata when the snapshot does not include it.</summary>
    /// <param name="entity">Detached entity facts.</param>
    /// <param name="layer">Assigned layer metadata.</param>
    /// <param name="id">Requested property.</param>
    public static DrawingValue? GetValue(EntitySnapshot entity, LayerSnapshot layer, DrawingPropertyId id)
    {
        if (entity.Properties?.TryGetValue(id, out var value) == true)
            return Normalize(value);

        return id == DrawingPropertyId.Layer ? new DrawingLayerValue(layer.Id, layer.Name) : null;
    }

    /// <summary>Reads an observed value attached to a displayed object row.</summary>
    /// <param name="node">Displayed row with detached primitive properties.</param>
    /// <param name="id">Requested property.</param>
    public static DrawingValue? GetValue(LensNode node, DrawingPropertyId id) =>
        node.Properties.IsDefaultOrEmpty
            ? null
            : Normalize(node.Properties.FirstOrDefault(property => property.Id == id)?.Value);

    /// <summary>Compares raw typed values; callers keep unavailable values last before applying sort direction.</summary>
    /// <remarks>
    /// Ascending type order is number, text, boolean, layer, color, lineweight, then transparency.
    /// Numbers compare unit first (count, distance, angle, scale), then raw value. Text uses ordinal
    /// case-insensitive ordering with case-sensitive ties; booleans put false first; layers compare name then ID.
    /// Assigned appearance compares assignment mode then raw identity, with explicit transparency increasing
    /// from opaque (0%) to transparent (100%). Null and nonfinite numbers compare after available values.
    /// </remarks>
    /// <param name="left">First observed value.</param>
    /// <param name="right">Second observed value.</param>
    public static int CompareValues(DrawingValue? left, DrawingValue? right)
    {
        left = Normalize(left);
        right = Normalize(right);

        if (left is null)
            return right is null ? 0 : 1;

        if (right is null)
            return -1;

        var kind = GetValueKind(left).CompareTo(GetValueKind(right));

        if (kind != 0)
            return kind;

        return (left, right) switch
        {
            (DrawingNumberValue first, DrawingNumberValue second) => first.Unit != second.Unit
                ? first.Unit.CompareTo(second.Unit)
                : first.Value.CompareTo(second.Value),
            (DrawingTextValue first, DrawingTextValue second) => CompareText(first.Text, second.Text),
            (DrawingBooleanValue first, DrawingBooleanValue second) => first.Value.CompareTo(second.Value),
            (DrawingLayerValue first, DrawingLayerValue second) => CompareLayers(first, second),
            (DrawingColorValue first, DrawingColorValue second) => CompareColors(first.Color, second.Color),
            (DrawingLineweightValue first, DrawingLineweightValue second) => CompareLineweights(
                first.Lineweight,
                second.Lineweight),
            (DrawingTransparencyValue first, DrawingTransparencyValue second) => CompareTransparency(
                first.Transparency,
                second.Transparency),
            _ => throw new ArgumentOutOfRangeException(nameof(left), left, "Unknown drawing value.")
        };
    }

    /// <summary>Creates details for observed properties, preserving present but unavailable values.</summary>
    /// <param name="entity">Detached entity facts.</param>
    /// <param name="layer">Assigned layer metadata.</param>
    public static ImmutableArray<DetailField> GetDetails(EntitySnapshot entity, LayerSnapshot layer) =>
    [
        .. (entity.Properties?.Keys ?? [])
        .Where(id => id != DrawingPropertyId.Layer)
        .OrderBy(id => id)
        .Select(id => new DetailField(
            GetLabel(id),
            string.Empty,
            DetailValueKind.TypedValue,
            GetValue(entity, layer, id)))
    ];

    private static DrawingValue? ReadValue(EntitySnapshot entity, DrawingPropertyId id) =>
        entity.Properties?.TryGetValue(id, out var value) == true ? Normalize(value) : null;

    private static DrawingValue? Normalize(DrawingValue? value) => value switch
    {
        DrawingNumberValue number when !number.Value.IsFinite() => null,
        DrawingNumberValue {Value: 0} number when BitConverter.DoubleToInt64Bits(number.Value) < 0 => number with
        {
            Value = 0
        },
        _ => value
    };

    private static int GetValueKind(DrawingValue value) => value switch
    {
        DrawingNumberValue => 0,
        DrawingTextValue => 1,
        DrawingBooleanValue => 2,
        DrawingLayerValue => 3,
        DrawingColorValue => 4,
        DrawingLineweightValue => 5,
        DrawingTransparencyValue => 6,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown drawing value.")
    };

    private static int CompareText(string? left, string? right)
    {
        var comparison = StringComparer.OrdinalIgnoreCase.Compare(left, right);

        return comparison == 0 ? StringComparer.Ordinal.Compare(left, right) : comparison;
    }

    private static int CompareLayers(DrawingLayerValue left, DrawingLayerValue right)
    {
        var comparison = CompareText(left.Name, right.Name);

        return comparison == 0 ? CompareText(left.Id.DisplayId, right.Id.DisplayId) : comparison;
    }

    private static int CompareColors(AssignedColor left, AssignedColor right)
    {
        var comparison = left.Kind.CompareTo(right.Kind);

        if (comparison != 0)
            return comparison;

        comparison = left.Value.CompareTo(right.Value);

        if (comparison != 0)
            return comparison;

        comparison = CompareText(left.BookName, right.BookName);

        return comparison == 0 ? CompareText(left.Name, right.Name) : comparison;
    }

    private static int CompareLineweights(AssignedLineweight left, AssignedLineweight right)
    {
        var comparison = left.Kind.CompareTo(right.Kind);

        return comparison == 0 ? left.HundredthsOfMillimeter.CompareTo(right.HundredthsOfMillimeter) : comparison;
    }

    private static int CompareTransparency(AssignedTransparency left, AssignedTransparency right)
    {
        var comparison = left.Kind.CompareTo(right.Kind);

        // Alpha is opacity; reversing it sorts the displayed transparency percentage in ascending order.
        return comparison == 0 ? right.Alpha.CompareTo(left.Alpha) : comparison;
    }
}