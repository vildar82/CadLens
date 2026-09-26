using Common;

namespace CadLens.Lenses.Tests;

internal sealed record TestLayerId(string Key) : ILayerId
{
    public string DisplayId => Key;
}
