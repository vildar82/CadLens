using System.Globalization;
using Common;

namespace CadLens.Preview;

internal sealed record EntityId(int Number) : IPlacedObjectId
{
    public string DisplayId => Number.ToString("X4", CultureInfo.InvariantCulture);
}
