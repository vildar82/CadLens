using System.Collections.Immutable;

namespace CadLens.Lenses;

/// <summary>Owns a generic exploration path without performing host actions.</summary>
public sealed class NavigationState
{
    private ImmutableArray<LensNode> _roots = [];
    private readonly List<LensNode> _path = [];

    /// <summary>The selected ancestors, from root group to current node.</summary>
    public IReadOnlyList<LensNode> Path => _path.AsReadOnly();

    /// <summary>The currently selected node, or null at the root.</summary>
    public LensNode? Current => _path.LastOrDefault();

    /// <summary>The children displayed at the current level.</summary>
    public ImmutableArray<LensNode> Items => Current?.Children ?? _roots;

    /// <summary>Whether an earlier object exists in the current object set.</summary>
    public bool CanPrevious => IsObject && ObjectIndex > 0;

    /// <summary>Whether a later object exists in the current object set.</summary>
    public bool CanNext => IsObject && ObjectIndex < Siblings.Length - 1;

    /// <summary>One-based object position, or zero outside object details.</summary>
    public int Position => IsObject ? ObjectIndex + 1 : 0;

    /// <summary>Number of objects in the current sibling set.</summary>
    public int ObjectCount => IsObject ? Siblings.Length : 0;

    private bool IsObject => Current is { Children.IsEmpty: true, Objects.Length: 1 };
    private ImmutableArray<LensNode> Siblings => _path.Count > 1 ? _path[^2].Children : _roots;
    private int ObjectIndex => Siblings.IndexOf(Current!);

    /// <summary>Reconciles the path with a refreshed result, retaining only valid ancestors.</summary>
    /// <param name="groups">Replacement root groups.</param>
    /// <param name="preservePath">False for document or space changes.</param>
    public void Reset(ImmutableArray<LensNode> groups, bool preservePath)
    {
        var identities = preservePath ? _path.Select(node => node.Id).ToArray() : [];
        _roots = groups;
        _path.Clear();

        foreach (var identity in identities)
        {
            if (!Enter(identity))
                break;
        }
    }

    /// <summary>Enters a child of the current level.</summary>
    /// <param name="identity">Child identity.</param>
    /// <returns>Whether the child still exists.</returns>
    public bool Enter(string identity)
    {
        var child = Items.FirstOrDefault(node => node.Id == identity);

        if (child is null)
            return false;

        _path.Add(child);
        return true;
    }

    /// <summary>Returns to an ancestor; zero denotes the root list.</summary>
    /// <param name="depth">Number of ancestors to retain.</param>
    public void GoBackTo(int depth)
    {
        if (depth >= 0 && depth < _path.Count)
            _path.RemoveRange(depth, _path.Count - depth);
    }

    /// <summary>Moves within the current object set without changing the drawing view.</summary>
    /// <param name="offset">Minus one for Previous; plus one for Next.</param>
    public void MoveObject(int offset)
    {
        if ((offset == -1 && CanPrevious) || (offset == 1 && CanNext))
            _path[^1] = Siblings[ObjectIndex + offset];
    }
}