namespace CadLens.Lenses;

/// <summary>Color assignment modes retained as primitive property values.</summary>
public enum AssignedColorKind
{
    /// <summary>Inherit the layer's color.</summary>
    ByLayer,
    /// <summary>Inherit the containing insertion's color.</summary>
    ByBlock,
    /// <summary>AutoCAD color index.</summary>
    Index,
    /// <summary>Explicit RGB color.</summary>
    TrueColor,
    /// <summary>A named color book entry.</summary>
    ColorBook,
    /// <summary>Another native assignment mode.</summary>
    Other
}

/// <summary>Color identity kept independently of localized display text.</summary>
/// <param name="Kind">Assignment mode.</param>
/// <param name="Value">Color index, packed RGB, or other native identity.</param>
/// <param name="Name">Color book and entry name when present.</param>
/// <param name="BookName">Color book name, kept separately from the entry name.</param>
public sealed record AssignedColor(AssignedColorKind Kind, int Value = 0, string? Name = null, string? BookName = null);

/// <summary>Lineweight assignment modes.</summary>
public enum AssignedLineweightKind
{
    /// <summary>Inherit the layer's lineweight.</summary>
    ByLayer,
    /// <summary>Inherit the containing insertion's lineweight.</summary>
    ByBlock,
    /// <summary>Use the drawing default.</summary>
    Default,
    /// <summary>Explicit lineweight.</summary>
    Explicit
}

/// <summary>Assigned lineweight, with explicit values in hundredths of a millimeter.</summary>
/// <param name="Kind">Assignment mode.</param>
/// <param name="HundredthsOfMillimeter">Explicit size in native lineweight units.</param>
public sealed record AssignedLineweight(AssignedLineweightKind Kind, int HundredthsOfMillimeter = 0);

/// <summary>Transparency assignment modes.</summary>
public enum AssignedTransparencyKind
{
    /// <summary>Inherit the layer's transparency.</summary>
    ByLayer,
    /// <summary>Inherit the containing insertion's transparency.</summary>
    ByBlock,
    /// <summary>Explicit opacity.</summary>
    Explicit
}

/// <summary>Assigned transparency; alpha 255 means fully opaque.</summary>
/// <param name="Kind">Assignment mode.</param>
/// <param name="Alpha">Explicit opacity.</param>
public sealed record AssignedTransparency(AssignedTransparencyKind Kind, byte Alpha = 255);