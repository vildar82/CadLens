using System.Globalization;
using CadLens.Common;

namespace CadLens.UI.Tests;

internal sealed record TestEntityId(int Number) : IPlacedObjectId
{
    public string DisplayId => Number.ToString(CultureInfo.InvariantCulture);
}
