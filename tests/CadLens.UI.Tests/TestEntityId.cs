using System.Globalization;
using Common;

namespace CadLens.UI.Tests;

internal sealed record TestEntityId(int Number) : IPlacedObjectId
{
    public string DisplayId => Number.ToString(CultureInfo.InvariantCulture);
}
