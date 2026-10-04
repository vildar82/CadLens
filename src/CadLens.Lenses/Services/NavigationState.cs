using System.Collections.Immutable;

namespace CadLens.Lenses;

/// <summary>Owns a generic exploration path without performing host actions.</summary>
public sealed class NavigationState
{
    private ImmutableArray<LensNode> _roots = [];
    private ImmutableArray<LensNode> _siblings = [];
    private readonly List<LensNode> _path = [];
    private Func<IEnumerable<LensNode>, IEnumerable<LensNode>> _itemOrder = items => items;

    /// <summary>The selected ancestors, from root group to current node.</summary>
    public IReadOnlyList<LensNode> Path => _path.AsReadOnly();

    /// <summary>The currently selected node, or null at the root.</summary>
    public LensNode? Current => _path.LastOrDefault();

    /// <summary>The children displayed at the current level.</summary>
    public ImmutableArray<LensNode> Items { get; private set; } = [];

    /// <summary>Whether an earlier object exists in the current object set.</summary>
    public bool CanPrevious => IsObject && ObjectIndex > 0;

    /// <summary>Whether a later object exists in the current object set.</summary>
    public bool CanNext => IsObject && ObjectIndex < Siblings.Length - 1;

    /// <summary>One-based object position, or zero outside object details.</summary>
    public int Position => IsObject ? ObjectIndex + 1 : 0;

    /// <summary>Number of objects in the current sibling set.</summary>
    public int ObjectCount => IsObject ? Siblings.Length : 0;

    private bool IsObject => Current is { } node && IsObjectNode(node);
    private ImmutableArray<LensNode> Siblings => _siblings;
    private int ObjectIndex { get; set; } = -1;

    /// <summary>Reconciles the path with a refreshed result, retaining only valid ancestors.</summary>
    /// <param name="groups">Replacement root groups.</param>
    /// <param name="preservePath">False for document or space changes.</param>
    public void Reset(ImmutableArray<LensNode> groups, bool preservePath)
    {
        var identities = preservePath ? _path.Select(node => node.Id).ToArray() : [];
        var selectedObject = preservePath && IsObject ? Current!.Objects[0] : null;
        _roots = groups;
        _path.Clear();

        if (selectedObject is not null && TryRestoreObject(groups, selectedObject))
        {
            RefreshProjection();
            return;
        }

        RefreshProjection();

        foreach (var identity in identities)
        {
            if (!Enter(identity))
                break;
        }
    }

    /// <summary>Uses the explorer's displayed ordering for lists and Previous/Next navigation.</summary>
    /// <param name="order">Current sorting projection; it must preserve the supplied members.</param>
    public void SetItemOrder(Func<IEnumerable<LensNode>, IEnumerable<LensNode>> order)
    {
        _itemOrder = order;
        RefreshProjection();
    }

    private void RefreshProjection()
    {
        Items = [.. _itemOrder(Current?.Children ?? _roots)];
        _siblings = IsObject ? [.. _itemOrder(_path.Count > 1 ? _path[^2].Children : _roots)] : [];
        ObjectIndex = IsObject ? _siblings.IndexOf(Current!) : -1;
    }

    private bool TryRestoreObject(ImmutableArray<LensNode> nodes, CadLens.Common.IPlacedObjectId identity)
    {
        foreach (var node in nodes)
        {
            _path.Add(node);

            if (IsObjectNode(node) && node.Objects[0].Equals(identity))
                return true;

            if (TryRestoreObject(node.Children, identity))
                return true;

            _path.RemoveAt(_path.Count - 1);
        }

        return false;
    }

    private static bool IsObjectNode(LensNode node) =>
        node.Kind == LensNodeKind.Object ||
        node is {Kind: LensNodeKind.GenericGroup, Children.IsEmpty: true, Objects.Length: 1};

    /// <summary>Enters a child of the current level.</summary>
    /// <param name="identity">Child identity.</param>
    /// <returns>Whether the child still exists.</returns>
    public bool Enter(string identity)
    {
        var child = Items.FirstOrDefault(node => node.Id == identity);

        if (child is null)
            return false;

        _path.Add(child);
        RefreshProjection();
        return true;
    }

    /// <summary>Returns to an ancestor; zero denotes the root list.</summary>
    /// <param name="depth">Number of ancestors to retain.</param>
    public void GoBackTo(int depth)
    {
        if (depth < 0 || depth >= _path.Count)
            return;

        _path.RemoveRange(depth, _path.Count - depth);
        RefreshProjection();
    }

    /// <summary>Moves within the current object set without changing the drawing view.</summary>
    /// <param name="offset">Minus one for Previous; plus one for Next.</param>
    public void MoveObject(int offset)
    {
        var canMove = offset switch {-1 => CanPrevious, 1 => CanNext, _ => false};

        if (!canMove)
            return;

        ObjectIndex += offset;
        _path[^1] = Siblings[ObjectIndex];
    }
}