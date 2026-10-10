namespace CadLens.UI;

/// <summary>Persistent controls for one drawing grouping; drawing and navigation state remain transient.</summary>
internal sealed record LensPreferences(
    bool IsAutoFocus = false,
    bool IsAutoSelect = false,
    bool IsAutoIsolation = false,
    string[]? EnabledFilters = null,
    string? SearchText = null,
    bool IsCountSortActive = false,
    bool SortDescending = false,
    Dictionary<string, string[]>? PropertyGrouping = null,
    Dictionary<string, string>? DisplayProperties = null,
    int AutoLoadObjectLimit = LensPreferences.DefaultAutoLoadObjectLimit)
{
    internal const int DefaultAutoLoadObjectLimit = 10_000;
}
