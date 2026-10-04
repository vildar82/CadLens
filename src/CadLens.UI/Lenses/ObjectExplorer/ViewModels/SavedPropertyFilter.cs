using System.Text.Json;
using CadLens.Lenses;

namespace CadLens.UI;

internal sealed record SavedPropertyFilter(
    string Name,
    string TypeKey,
    DrawingPropertyId PropertyId,
    DrawingFilterOperator Operator,
    string ValueKind,
    JsonElement Value)
{
    internal static SavedPropertyFilter Create(string name, string typeKey, DrawingPropertyFilter filter)
    {
        var (kind, value) = filter.Value switch
        {
            DrawingNumberValue number => (number.Unit.ToString(), JsonSerializer.SerializeToElement(number.Value)),
            DrawingTextValue text => (text.IsApplicationText ? "ApplicationText" : "Text", JsonSerializer.SerializeToElement(text.Text)),
            DrawingBooleanValue boolean => ("Boolean", JsonSerializer.SerializeToElement(boolean.Value)),
            DrawingLayerValue layer => ("Layer", JsonSerializer.SerializeToElement(layer.Name)),
            DrawingColorValue color => ("Color", JsonSerializer.SerializeToElement(color.Color)),
            DrawingLineweightValue lineweight => ("Lineweight", JsonSerializer.SerializeToElement(lineweight.Lineweight)),
            DrawingTransparencyValue transparency => ("Transparency", JsonSerializer.SerializeToElement(transparency.Transparency)),
            _ => throw new ArgumentOutOfRangeException(nameof(filter))
        };

        return new SavedPropertyFilter(name, typeKey, filter.PropertyId, filter.Operator, kind, value);
    }

    internal DrawingPropertyFilter? Read(IEnumerable<LensNode> objects)
    {
        try
        {
            DrawingValue? value = ValueKind switch
            {
                "Text" or "ApplicationText" when Value.ValueKind == JsonValueKind.String =>
                    new DrawingTextValue(Value.GetString()!, ValueKind == "ApplicationText"),
                "Boolean" when Value.ValueKind is JsonValueKind.True or JsonValueKind.False =>
                    new DrawingBooleanValue(Value.GetBoolean()),
                "Layer" when Value.ValueKind == JsonValueKind.String => objects
                    .Select(node => DrawingProperties.GetValue(node, DrawingPropertyId.Layer))
                    .OfType<DrawingLayerValue>()
                    .FirstOrDefault(layer => string.Equals(layer.Name, Value.GetString(), StringComparison.OrdinalIgnoreCase)),
                "Color" => Value.Deserialize<AssignedColor>() is { } color ? new DrawingColorValue(color) : null,
                "Lineweight" => Value.Deserialize<AssignedLineweight>() is { } weight ? new DrawingLineweightValue(weight) : null,
                "Transparency" => Value.Deserialize<AssignedTransparency>() is { } transparency
                    ? new DrawingTransparencyValue(transparency)
                    : null,
                _ when Enum.TryParse<DrawingUnit>(ValueKind, out var unit) &&
#if NETFRAMEWORK
                       Enum.IsDefined(typeof(DrawingUnit), unit) &&
#else
                       Enum.IsDefined(unit) &&
#endif
                       Value.ValueKind == JsonValueKind.Number =>
                    new DrawingNumberValue(Value.GetDouble(), unit),
                _ => null
            };

            return value is null ? null : new DrawingPropertyFilter(PropertyId, Operator, value);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
