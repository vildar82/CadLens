using System.Globalization;
using Common;

namespace CadLens.Preview;

internal sealed record LayerId(int Number) : ILayerId
{
    public string DisplayId => Number.ToString(CultureInfo.InvariantCulture);
}
