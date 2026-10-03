using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using CadLens.Lenses;

namespace CadLens.UI;

/// <summary>Dynamic app-text binding that follows the local language without recreating views.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class LocalizeExtension : MarkupExtension
{
    /// <summary>Creates a translation of a fixed app-owned phrase.</summary>
    /// <param name="text">English resource key.</param>
    public LocalizeExtension(string text) => Text = text;

    /// <summary>Creates a translation of app-owned metadata from a binding.</summary>
    public LocalizeExtension() { }

    /// <summary>Fixed English phrase.</summary>
    public string? Text { get; set; }

    /// <summary>Binding to app-owned metadata; never use for drawing names.</summary>
    public Binding? Binding { get; set; }

    /// <summary>Whether the bound object is a detail field whose raw drawing value must be preserved.</summary>
    public bool DetailValue { get; set; }

    /// <summary>Optional localized format applied to a numeric binding.</summary>
    public string? Format { get; set; }

    /// <inheritdoc />
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new MultiBinding
        {
            Mode = BindingMode.OneWay,
            Converter = new TextConverter(Text, DetailValue, Format)
        };
        binding.Bindings.Add(Binding ?? new Binding { Source = Text });
        binding.Bindings.Add(new Binding(nameof(UiText.Culture)) { Source = UiText.Current });

        return binding.ProvideValue(serviceProvider);
    }

    private sealed class TextConverter(string? text, bool detailValue, string? format) : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var value = values[0];

            if (value == DependencyProperty.UnsetValue || value is null)
                return "";

            if (detailValue && value is DetailField field)
                return field.Label is "Visibility" or "Locked" ? UiText.Current.Get(field.Value) : field.Value;

            if (format is not null)
                return string.Format(UiText.Current.Culture, UiText.Current.Get(format), value);

            return UiText.Current.Get(value as string ?? text ?? "");
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}