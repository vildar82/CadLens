using CadLens.Common;
using CadLens.Lenses;
using CommunityToolkit.Mvvm.Input;

namespace CadLens.UI;

public sealed partial class ObjectExplorerViewModel
{
    private BlockParent? _blockParent;

    private LensNode? IsolationTarget => Current ?? _blockParent?.Navigation.Current;

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
        var cleared = await _actions.SelectAsync([], cancellationToken, _progress);
        cancellationToken.ThrowIfCancellationRequested();

        if (cleared is not HostResult<bool>.Success {Value: true})
            return DescribeSelection(cleared, true);

        HostResult<LensPresentation> result;

        try
        {
            result = await _actions.ReadBlockContentsAsync(
                block.Objects[0],
                _enabledFilters,
                cancellationToken,
                StartReadProgress(cancellationToken));
        }
        finally
        {
            StopReadProgress();
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (result is not HostResult<LensPresentation>.Success success)
            return ((HostResult<LensPresentation>.Unavailable) result).Reason;

        if (!entering)
            return await ApplyPresentationAsync(success.Value, null, cancellationToken);

        _blockParent = new BlockParent(_inventory!, _navigation, SearchText);
        _navigation = new NavigationState();
        _searchText = "";
        OnPropertyChanged(nameof(SearchText));

        return await ApplyPresentationAsync(success.Value, null, cancellationToken);
    }

    private async Task<UiMessage> IsolateCurrentAsync(CancellationToken cancellationToken) =>
        await _actions.IsolateObjectsAsync(IsolationTarget!.Objects, cancellationToken, _progress);

    private async Task<UiMessage> ApplyRootEffectsAsync(CancellationToken cancellationToken)
    {
        if (!IsAutoIsolation || IsolationTarget is null)
            return await ClearEffectsAsync(cancellationToken);

        var cleared = await _actions.SelectAsync([], cancellationToken, _progress);
        cancellationToken.ThrowIfCancellationRequested();

        if (cleared is not HostResult<bool>.Success {Value: true})
            return DescribeSelection(cleared, true);

        return await IsolateCurrentAsync(cancellationToken);
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

        var cancellationToken = _pendingRequest?.Token ?? CancellationToken.None;
        var presentation = DrawingLensProvider.Build(
            parent.Inventory,
            _grouping,
            _enabledFilters,
            cancellationToken: cancellationToken,
            progress: _progress);
        var groups = HasPropertyOptions
            ? DrawingLensProvider.Build(
                parent.Inventory,
                _grouping,
                _enabledFilters,
                _propertyGrouping,
                _propertyFilters,
                progress: _progress,
                cancellationToken: cancellationToken).Groups
            : presentation.Groups;
        cancellationToken.ThrowIfCancellationRequested();
        _blockParent = null;
        _navigation = parent.Navigation;
        _searchText = parent.SearchText;
        OnPropertyChanged(nameof(SearchText));
        ApplyPresentation(presentation, groups);
    }

    private sealed record BlockParent(DrawingInventory Inventory, NavigationState Navigation, string SearchText);
}