namespace CadLens.Lenses;

/// <summary>The origin of a built-in or drawing-owned property.</summary>
public enum DrawingPropertySource
{
    /// <summary>A known entity property.</summary>
    BuiltIn,
    /// <summary>An attached block attribute identified by its exact tag.</summary>
    Attribute,
    /// <summary>A dynamic block property identified by its exact name.</summary>
    DynamicBlock
}

/// <summary>A property identity that keeps drawing-owned names separate from built-in properties.</summary>
public readonly record struct DrawingPropertyKey : IComparable<DrawingPropertyKey>
{
    private DrawingPropertyKey(DrawingPropertySource source, DrawingPropertyId? builtIn, string? name)
    {
        Source = source;
        BuiltIn = builtIn;
        Name = name ?? string.Empty;
    }

    /// <summary>Origin of the property.</summary>
    public DrawingPropertySource Source { get; }

    /// <summary>Known property identity, or null for drawing-owned names.</summary>
    public DrawingPropertyId? BuiltIn { get; }

    /// <summary>Exact drawing-owned name, or an empty string for built-in properties.</summary>
    public string Name => field ?? string.Empty;

    /// <summary>Creates an identity for an exact attribute tag.</summary>
    /// <param name="tag">Nonempty drawing-owned tag.</param>
    public static DrawingPropertyKey ForAttribute(string tag) => Named(DrawingPropertySource.Attribute, tag);

    /// <summary>Creates an identity for an exact dynamic block property name.</summary>
    /// <param name="name">Nonempty drawing-owned name.</param>
    public static DrawingPropertyKey ForDynamicBlock(string name) => Named(DrawingPropertySource.DynamicBlock, name);

    /// <summary>Uses an existing built-in property as a selected property.</summary>
    /// <param name="id">Known property identity.</param>
    public static implicit operator DrawingPropertyKey(DrawingPropertyId id) =>
        new(DrawingPropertySource.BuiltIn, id, null);

    /// <summary>Parses the stable persisted identity without normalizing drawing-owned names.</summary>
    /// <param name="text">Previously persisted identity.</param>
    /// <param name="key">Parsed identity when successful.</param>
    public static bool TryParse(string? text, out DrawingPropertyKey key)
    {
        key = default;

        if (text is not {Length: > 0})
            return false;

        if (text.StartsWith("attribute:", StringComparison.Ordinal) && text.Length > 10)
        {
            key = ForAttribute(text[10..]);
            return true;
        }

        if (text.StartsWith("dynamic:", StringComparison.Ordinal) && text.Length > 8)
        {
            key = ForDynamicBlock(text[8..]);
            return true;
        }

        if (!Enum.TryParse<DrawingPropertyId>(text, out var id) || id.ToString() != text ||
#if NETFRAMEWORK
            !Enum.IsDefined(typeof(DrawingPropertyId), id))
#else
            !Enum.IsDefined(id))
#endif
            return false;

        key = id;
        return true;
    }

    /// <inheritdoc />
    public int CompareTo(DrawingPropertyKey other)
    {
        var source = Source.CompareTo(other.Source);

        if (source != 0)
            return source;

        return Source == DrawingPropertySource.BuiltIn
            ? Nullable.Compare(BuiltIn, other.BuiltIn)
            : StringComparer.Ordinal.Compare(Name, other.Name);
    }

    /// <inheritdoc />
    public override string ToString() => Source switch
    {
        DrawingPropertySource.BuiltIn => BuiltIn?.ToString() ?? string.Empty,
        DrawingPropertySource.Attribute => $"attribute:{Name}",
        DrawingPropertySource.DynamicBlock => $"dynamic:{Name}",
        _ => throw new ArgumentOutOfRangeException(nameof(Source), Source, "Unknown property source.")
    };

    private static DrawingPropertyKey Named(DrawingPropertySource source, string name) => string.IsNullOrEmpty(name)
        ? throw new ArgumentException("A drawing-owned property name cannot be empty.", nameof(name))
        : new DrawingPropertyKey(source, null, name);
}
