using Common;

namespace CadLens.Lenses.Tests;

internal sealed record TestEntityId(string Key) : IPlacedObjectId
{
    public string DisplayId => Key;
}
