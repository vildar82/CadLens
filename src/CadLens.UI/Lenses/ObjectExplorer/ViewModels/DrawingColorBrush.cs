using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;

namespace CadLens.UI;

/// <summary>Shows detached row colors, adapting black and white to the current theme.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class DrawingColorBrush : MarkupExtension
{
    /// <inheritdoc />
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new MultiBinding {Mode = BindingMode.OneWay, Converter = new ColorConverter()};
        binding.Bindings.Add(new Binding("DisplayColor"));
        binding.Bindings.Add(new Binding(nameof(Control.Foreground))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(UserControl), 1)
        });

        return binding.ProvideValue(serviceProvider);
    }

    private sealed class ColorConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values[0] is not int rgb)
                return Brushes.Transparent;

            if (rgb is 0 or 0xFFFFFF && values[1] is Brush foreground)
                return foreground;

            var brush = new SolidColorBrush(Color.FromRgb((byte) (rgb >> 16), (byte) (rgb >> 8), (byte) rgb));
            brush.Freeze();

            return brush;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
