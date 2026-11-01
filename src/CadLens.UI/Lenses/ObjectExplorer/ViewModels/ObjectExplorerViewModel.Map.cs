using System.Collections.Immutable;
using CadLens.Common;
using CadLens.Lenses;
using CommunityToolkit.Mvvm.Input;

namespace CadLens.UI;

public sealed partial class ObjectExplorerViewModel
{
    private const int MapOtherColor = 0x808080;

    private static readonly ImmutableArray<int> MapPalette =
    [
        0xE6194B, 0x3CB44B, 0xFFE119, 0x4363D8, 0xF58231, 0xB66DFF,
        0x42D4F4, 0xF032E6, 0xBFEF45, 0xFABED4, 0x469990, 0x9A6324
    ];

    private ImmutableArray<LensNode> _mapRows = [];
    private Dictionary<string, int> _mapColors = new();
    private ImmutableArray<LensNode> _legendSource;
    private ImmutableArray<LensNode> _legendItems;

    /// <summary>Whether the drawing is colored by the current list rows.</summary>
    public bool IsMapMode
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>Switches the temporary coloring of the drawing on or off.</summary>
    public IAsyncRelayCommand ToggleMapCommand { get; }

    // Opening a row keeps the root colors; only combined properties make a new map.
    private ImmutableArray<LensNode>? MapRows
    {
        get
        {
            if (IsBrowsingBlockContents)
                return null;

            for (var depth = _navigation.Path.Count - 1; depth >= 0; depth--)
            {
                var children = _navigation.Path[depth].Children;

                if (!children.IsDefaultOrEmpty && children[0].Kind == LensNodeKind.PropertyGroup)
                    return children;
            }

            return Groups;
        }
    }

    private ImmutableArray<LensNode> ColorLegend(ImmutableArray<LensNode> items)
    {
        if (_mapColors.Count == 0 || IsBrowsingBlockContents)
            return items;

        if (items.Equals(_legendSource))
            return _legendItems;

        _legendSource = items;
        _legendItems =
        [
            .. items.Select(node =>
                node.Kind != LensNodeKind.Object && _mapColors.TryGetValue(node.Id, out var color)
                    ? node with {DisplayColor = color}
                    : node)
        ];

        return _legendItems;
    }

    private Task<UiMessage> ToggleMapAsync(CancellationToken cancellationToken)
    {
        IsMapMode = !IsMapMode;

        return Task.FromResult<UiMessage>(
            IsMapMode ? "Map on. List rows show the drawing colors." : "Map off. Original colors restored.");
    }

    private async Task<UiMessage> UpdateMapAsync(UiMessage message, CancellationToken cancellationToken)
    {
        if ((IsMapMode ? MapRows : ImmutableArray<LensNode>.Empty) is not { } rows || rows.Equals(_mapRows))
            return message;

        var colors = AssignMapColors(rows);
        _mapRows = rows;
        SetMapColors(colors);
        var result = await _actions.PaintAsync(
            [.. rows.Select(row => new PaintGroup(row.Objects, colors[row.Id]))],
            cancellationToken,
            _progress);

        if (result is HostResult<bool>.Success {Value: true})
            return message;

        IsMapMode = false;
        ForgetMap();

        return result is HostResult<bool>.Unavailable unavailable
            ? new UiMessage("Map unavailable: {0}", new UiMessage(unavailable.Reason))
            : "Map did not complete.";
    }

    private async Task<HostResult<bool>> ClearMapAsync(HostResult<bool> cleanup, CancellationToken cancellationToken)
    {
        if (_mapRows.IsEmpty)
            return cleanup;

        var result = await _actions.PaintAsync([], cancellationToken, _progress);
        ForgetMap();

        return cleanup is HostResult<bool>.Success {Value: true} ? result : cleanup;
    }

    private void ForgetMap()
    {
        _mapRows = [];
        SetMapColors(new Dictionary<string, int>());
    }

    private void SetMapColors(Dictionary<string, int> colors)
    {
        _mapColors = colors;
        _legendSource = default;
        OnPropertyChanged(nameof(Items));
    }

    private static Dictionary<string, int> AssignMapColors(ImmutableArray<LensNode> rows)
    {
        var colors = new Dictionary<string, int>();
        var index = 0;

        foreach (var row in rows.OrderByDescending(row => row.Count).ThenBy(row => row.Id, StringComparer.Ordinal))
        {
            colors[row.Id] = index < MapPalette.Length ? MapPalette[index] : MapOtherColor;
            index++;
        }

        return colors;
    }
}