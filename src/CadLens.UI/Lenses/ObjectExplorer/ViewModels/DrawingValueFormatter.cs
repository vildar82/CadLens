using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using CadLens.Lenses;
using CadLens.Common;

namespace CadLens.UI;

/// <summary>Formats detached drawing facts with the current app language.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class DrawingValueFormatter : MarkupExtension
{
    /// <summary>Binding to a drawing node.</summary>
    public Binding? Binding { get; set; }

    /// <summary>Binding to the selected property for object rows.</summary>
    public Binding? PropertyBinding { get; set; }

    /// <summary>Whether to show the row metric instead of the node label.</summary>
    public bool Metric { get; set; }

    /// <summary>Whether to show the full selected object value or its unavailability explanation.</summary>
    public bool ValueToolTip { get; set; }

    /// <inheritdoc />
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new MultiBinding
        {
            Mode = BindingMode.OneWay,
            Converter = new NodeTextConverter(Metric, ValueToolTip)
        };
        binding.Bindings.Add(Binding ?? new Binding());
        binding.Bindings.Add(new Binding(nameof(UiText.Culture)) {Source = UiText.Current});
        binding.Bindings.Add(PropertyBinding ?? new Binding {Source = null});
        binding.Bindings.Add(PrecisionBinding());

        return binding.ProvideValue(serviceProvider);
    }

    /// <summary>Localizes the property source while preserving drawing-owned names.</summary>
    /// <param name="key">Identity of a built-in, attribute, or dynamic block property.</param>
    public static string FormatPropertyLabel(DrawingPropertyKey key) => key.Source switch
    {
        DrawingPropertySource.Attribute => new UiMessage("Attribute: {0}", key.Name).ToString(),
        DrawingPropertySource.DynamicBlock => new UiMessage("Dynamic block: {0}", key.Name).ToString(),
        _ => UiText.Current.Get(DrawingProperties.GetLabel(key))
    };

    /// <summary>Formats property labels without translating drawing-owned text.</summary>
    /// <param name="field">Detached detail field and optional property identity.</param>
    public static string FormatDetailLabel(DetailField field) => field.PropertyKey is { } key
        ? FormatPropertyLabel(key)
        : field.IsLabelRaw ? field.Label : UiText.Current.Get(field.Label);

    /// <summary>Formats composite group captions while preserving drawing-owned text.</summary>
    /// <param name="node">Node whose properties supply the group caption.</param>
    public static string FormatLabel(LensNode node) => FormatLabel(node, DrawingPrecision.Default);

    /// <summary>Formats a composite caption with the current drawing's numeric precision.</summary>
    /// <param name="node">Node whose properties supply the caption.</param>
    /// <param name="precision">Detached drawing precision.</param>
    public static string FormatLabel(LensNode node, DrawingPrecision precision) =>
        node is {Kind: LensNodeKind.PropertyGroup, Properties.IsDefaultOrEmpty: false}
            ? string.Join(
                " · ",
                node.Properties
                    .OrderBy(
                        property => FormatPropertyLabel(property.Id),
                        StringComparer.Create(UiText.Current.Culture, true))
                    .Select(property => $"{FormatPropertyLabel(property.Id)}: {FormatValue(property.Value, precision)}"))
            : node.Label;

    /// <summary>Formats a placed-object metric or the selectable group count.</summary>
    /// <param name="node">List row to display.</param>
    /// <param name="propertyId">Selected property, or the object's default measurement.</param>
    /// <param name="precision">Detached drawing precision.</param>
    public static string FormatMetric(
        LensNode node,
        DrawingPropertyKey? propertyId = null,
        DrawingPrecision? precision = null) =>
        node.Kind == LensNodeKind.Object
            ? FormatValue(
                propertyId is { } id ? DrawingProperties.GetValue(node, id) : node.RowMetric?.Value,
                precision)
            : node.Count.ToString("N0", UiText.Current.Culture);

    /// <summary>Formats a detail using its explicit value policy.</summary>
    /// <param name="field">Detached detail value and display kind.</param>
    /// <param name="precision">Detached drawing precision.</param>
    public static string FormatDetail(DetailField field, DrawingPrecision? precision = null) => field.ValueKind switch
    {
        DetailValueKind.TypedValue => FormatValue(field.TypedValue, precision),
        DetailValueKind.PrimitiveType when field.TypedValue is not null => FormatValue(field.TypedValue, precision),
        DetailValueKind.ApplicationText or DetailValueKind.LayerVisibility =>
            UiText.Current.Get(field.Value),
        _ => field.Value
    };

    /// <summary>Formats a typed value without changing grouping identity.</summary>
    /// <param name="value">Detached drawing value, or an unavailable value.</param>
    /// <param name="precision">Detached drawing precision.</param>
    public static string FormatValue(DrawingValue? value, DrawingPrecision? precision = null) => value switch
    {
        DrawingTextValue text => text.IsApplicationText ? UiText.Current.Get(text.Text) : text.Text,
        DrawingNumberValue number => FormatNumber(number, precision ?? DrawingPrecision.Default),
        DrawingBooleanValue boolean => UiText.Current.Get(boolean.Value ? "Yes" : "No"),
        DrawingColorValue color => FormatColor(color.Color),
        DrawingLineweightValue lineweight => FormatLineweight(lineweight.Lineweight),
        DrawingTransparencyValue transparency => FormatTransparency(transparency.Transparency),
        DrawingLayerValue layer => layer.Name,
        _ => "—"
    };

    internal static Binding PrecisionBinding() => new("DataContext.Precision")
    {
        RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ObjectExplorerView), 1)
    };

    private static string FormatNumber(DrawingNumberValue number, DrawingPrecision precision)
    {
        var value = number.Unit == DrawingUnit.Angle ? number.Value * (180 / Math.PI) : number.Value;

        if (!value.IsFinite())
            return "—";

        return number.Unit switch
        {
            DrawingUnit.Count => value.ToString("N0", UiText.Current.Culture),
            DrawingUnit.Angle => $"{FormatDecimal(value, precision.Angular)}°",
            _ => FormatDecimal(value, precision.Linear)
        };
    }

    private static string FormatDecimal(double value, int precision)
    {
        var digits = Math.Min(Math.Max(precision, 0), 8);
        var rounded = Math.Round(value, digits, MidpointRounding.AwayFromZero);
        var format = digits == 0 ? "0" : $"0.{new string('#', digits)}";

        return (rounded == 0 ? 0 : rounded).ToString(format, UiText.Current.Culture);
    }

    private static string FormatColor(AssignedColor color) => color.Kind switch
    {
        AssignedColorKind.ByLayer => UiText.Current.Get("ByLayer"),
        AssignedColorKind.ByBlock => UiText.Current.Get("ByBlock"),
        AssignedColorKind.Index => $"ACI {color.Value.ToString(UiText.Current.Culture)}",
        AssignedColorKind.TrueColor => $"#{color.Value.ToString("X6", CultureInfo.InvariantCulture)}",
        AssignedColorKind.ColorBook when color.BookName is not null => $"{color.BookName}: {color.Name}",
        _ => color.Name ?? UiText.Current.Get("Other")
    };

    private static string FormatLineweight(AssignedLineweight lineweight) => lineweight.Kind switch
    {
        AssignedLineweightKind.ByLayer => UiText.Current.Get("ByLayer"),
        AssignedLineweightKind.ByBlock => UiText.Current.Get("ByBlock"),
        AssignedLineweightKind.Default => UiText.Current.Get("Default"),
        _ => string.Format(
            UiText.Current.Culture,
            UiText.Current.Get("{0:0.00} mm"),
            lineweight.HundredthsOfMillimeter / 100d)
    };

    private static string FormatTransparency(AssignedTransparency transparency) => transparency.Kind switch
    {
        AssignedTransparencyKind.ByLayer => UiText.Current.Get("ByLayer"),
        AssignedTransparencyKind.ByBlock => UiText.Current.Get("ByBlock"),
        _ => $"{((255 - transparency.Alpha) * 100d / 255).ToString("0.#", UiText.Current.Culture)}%"
    };

    private sealed class NodeTextConverter(bool metric, bool valueToolTip) : IMultiValueConverter
    {
        public object Convert(object?[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values[0] is not LensNode node)
                return "";

            DrawingPropertyKey? propertyId = values[2] is DrawingPropertyKey selected ? selected : null;
            var precision = values[3] as DrawingPrecision ?? DrawingPrecision.Default;

            if (!valueToolTip)
                return metric ? FormatMetric(node, propertyId, precision) : FormatLabel(node, precision);

            if (node.Kind != LensNodeKind.Object)
                return DependencyProperty.UnsetValue;

            var value = propertyId is { } id ? DrawingProperties.GetValue(node, id) : node.RowMetric?.Value;
            return value is null ? UiText.Current.Get("Unavailable") : FormatValue(value, precision);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}