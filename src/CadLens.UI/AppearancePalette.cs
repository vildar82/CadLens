using System.Windows;
using System.Windows.Media;

namespace CadLens.UI;

internal static class AppearancePalette
{
    internal static void Apply(
        ResourceDictionary resources,
        string theme,
        string palette,
        string accent,
        bool hostIsLight)
    {
        var isLight = theme == "Light" || (theme == "Follow AutoCAD" && hostIsLight);
        var surfaces = (palette, isLight) switch
        {
            ("Graphite", true) => new Surfaces(
                "#F3F3F4",
                "#FFFFFF",
                "#E7E7EB",
                "#CECED5",
                "#DFDFE6",
                "#25252B",
                "#62626D"),
            ("Graphite", false) => new Surfaces(
                "#19191D",
                "#25252B",
                "#202025",
                "#494953",
                "#34343D",
                "#F0F0F4",
                "#B1B1C0"),
            ("Paper", true) => new Surfaces(
                "#F5F0E8",
                "#FFFBF4",
                "#EEE6D9",
                "#CEC1AE",
                "#E8DDCD",
                "#302920",
                "#6A5D48"),
            ("Paper", false) => new Surfaces(
                "#211E19",
                "#2D2821",
                "#28231D",
                "#514838",
                "#3D352A",
                "#F7F0E3",
                "#C0AF94"),
            (_, true) => new Surfaces(
                "#EEF4F7",
                "#FFFFFF",
                "#E2EDF1",
                "#B9CCD5",
                "#D5E5EC",
                "#192C36",
                "#48616F"),
            _ => new Surfaces(
                "#10171E",
                "#1B2932",
                "#18232C",
                "#42545E",
                "#2A3C46",
                "#EEF5F8",
                "#9EB5BE")
        };
        var accentColor = (accent, isLight) switch
        {
            ("Blue", true) => "#185AB6",
            ("Blue", false) => "#88BDFF",
            ("Violet", true) => "#7544AA",
            ("Violet", false) => "#CEAEFA",
            ("Amber", true) => "#845200",
            ("Amber", false) => "#F0C46B",
            (_, true) => "#006E5C",
            _ => "#78E3CE"
        };

        SetBrush(resources, "QuietBackground", surfaces.Background);
        SetBrush(resources, "QuietSurface", surfaces.Surface);
        SetBrush(resources, "QuietHeader", surfaces.Header);
        SetBrush(resources, "QuietBorder", surfaces.Border);
        SetBrush(resources, "QuietHover", surfaces.Hover);
        SetBrush(resources, "QuietPressed", surfaces.Background);
        SetBrush(resources, "QuietText", surfaces.Text);
        SetBrush(resources, "QuietMuted", surfaces.Muted);
        SetBrush(resources, "QuietDisabledText", surfaces.Muted);
        SetBrush(resources, "QuietDisabledSurface", surfaces.Header);
        SetBrush(resources, "QuietAccent", accentColor);
        SetBrush(resources, "QuietFocus", accentColor);

        var selected = Blend(
            (Color) ColorConverter.ConvertFromString(surfaces.Surface),
            (Color) ColorConverter.ConvertFromString(accentColor),
            isLight ? 0.12 : 0.16);
        var selectedBrush = new SolidColorBrush(selected);
        selectedBrush.Freeze();
        resources["QuietSelected"] = selectedBrush;
    }

    private static void SetBrush(ResourceDictionary resources, string key, string color)
    {
        var brush = new SolidColorBrush((Color) ColorConverter.ConvertFromString(color));
        brush.Freeze();
        resources[key] = brush;
    }

    private static Color Blend(Color surface, Color accent, double amount) => Color.FromRgb(
        (byte) (surface.R + (accent.R - surface.R) * amount),
        (byte) (surface.G + (accent.G - surface.G) * amount),
        (byte) (surface.B + (accent.B - surface.B) * amount));

    private sealed record Surfaces(
        string Background,
        string Surface,
        string Header,
        string Border,
        string Hover,
        string Text,
        string Muted);
}
