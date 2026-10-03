namespace CadLens.Lenses;

/// <summary>Drawing display precision; stored property values remain exact.</summary>
/// <param name="Linear">Fractional digits for linear measurements.</param>
/// <param name="Angular">Fractional digits for angular measurements.</param>
public sealed record DrawingPrecision(int Linear = 4, int Angular = 0)
{
    /// <summary>Default precision when a presentation does not supply drawing settings.</summary>
    public static DrawingPrecision Default { get; } = new();
}