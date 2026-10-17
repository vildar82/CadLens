using CadLens.Common;
using CadLens.Lenses;
using CommunityToolkit.Mvvm.Input;

namespace CadLens.UI;

public sealed partial class ObjectExplorerViewModel
{
    private BlockParent? _blockParent;

    /// <summary>
    /// Whether the explorer is showing the recursive contents of one block occurrence.
    /// </summary>
    public bool IsBrowsingBlockContents => _blockParent is not null;

    /// <summary>
    /// Whether the current Object Types detail supports browsing local block contents.
    /// </summary>
    public bool CanBrowseBlockContents => _grouping == DrawingGrouping.ObjectTypes && !IsBrowsingBlockContents &&
                                          Current is {Kind: LensNodeKind.Object} node &&
                                          node.Actions.Contains(LensAction.BrowseContents);

    /// <summary>
    /// Opens the current block's contents grouped by object type.
    /// </summary>
    public IAsyncRelayCommand BrowseBlockContentsCommand { get; }

    private async Task<UiMessage> ReadBlockContentsAsync(bool entering, CancellationToken cancellationToken)
    {
        var block = entering ? Current! : _blockParent!.Navigation.Current!;
        var cleared = await _actions.ClearAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (cleared is not HostResult<bool>.Success {Value: true})
            return DescribeCleanup(cleared);

        var result = await _actions.ReadBlockContentsAsync(block.Objects[0], _enabledFilters, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (result is not HostResult<LensPresentation>.Success success)
            return ((HostResult<LensPresentation>.Unavailable) result).Reason;

        if (!entering)
            return await ApplyPresentationAsync(success.Value, cancellationToken);

        _blockParent = new BlockParent(_inventory!, _navigation, SearchText);
        _navigation = new NavigationState();
        _searchText = "";
        OnPropertyChanged(nameof(SearchText));

        return await ApplyPresentationAsync(success.Value, cancellationToken);
    }

    private void GoBack()
    {
        if (IsBrowsingBlockContents && Current is null)
        {
            RestoreBlockParent();
            NotifyNavigation();
            return;
        }

        GoBackTo(_navigation.Path.Count - 1);
    }

    private void GoToRoot()
    {
        RestoreBlockParent();
        GoBackTo(0);
    }

    private void GoToBreadcrumb(LensNode node)
    {
        if (_blockParent is { } parent && parent.Navigation.Path.Contains(node))
        {
            var depth = Array.IndexOf([.. parent.Navigation.Path], node) + 1;
            RestoreBlockParent();
            GoBackTo(depth);
            return;
        }

        GoBackTo(Array.IndexOf([.. _navigation.Path], node) + 1);
    }

    private void RestoreBlockParent()
    {
        if (_blockParent is not { } parent)
            return;

        _blockParent = null;
        _navigation = parent.Navigation;
        _searchText = parent.SearchText;
        OnPropertyChanged(nameof(SearchText));
        ApplyPresentation(DrawingLensProvider.Build(
            parent.Inventory,
            _grouping,
            _enabledFilters,
            _propertyGrouping,
            _propertyFilters));
    }

    private sealed record BlockParent(DrawingInventory Inventory, NavigationState Navigation, string SearchText);
}