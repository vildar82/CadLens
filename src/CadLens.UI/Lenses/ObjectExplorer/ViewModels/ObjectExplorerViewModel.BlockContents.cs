using CadLens.Common;
using CadLens.Lenses;
using CommunityToolkit.Mvvm.Input;

namespace CadLens.UI;

public sealed partial class ObjectExplorerViewModel
{
    private BlockParent? _blockParent;

    private DrawingGrouping CurrentGrouping => IsBrowsingBlockContents ? DrawingGrouping.ObjectTypes : _grouping;

    private LensNode? IsolationTarget => Current ?? _blockParent?.Navigation.Current;

    /// <summary>
    /// Whether the explorer is showing the recursive contents of one block occurrence.
    /// </summary>
    public bool IsBrowsingBlockContents => _blockParent is not null;

    /// <summary>
    /// Whether the current block detail supports browsing local block contents.
    /// </summary>
    public bool CanBrowseBlockContents => !IsBrowsingBlockContents &&
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
            return await ApplyPresentationAsync(success.Value, DrawingGrouping.ObjectTypes, null, cancellationToken);

        _blockParent = new BlockParent(_inventory!, _navigation, SearchText);
        _navigation = new NavigationState();
        _searchText = "";
        OnPropertyChanged(nameof(SearchText));

        return await ApplyPresentationAsync(success.Value, DrawingGrouping.ObjectTypes, null, cancellationToken);
    }

    private async Task<UiMessage> IsolateCurrentAsync(CancellationToken cancellationToken) =>
        await _actions.IsolateObjectsAsync(IsolationTarget!.Objects, cancellationToken, _progress);

    private async Task<UiMessage> ApplyRootEffectsAsync(CancellationToken cancellationToken)
    {
        // Only a selection that followed navigation ends at the root; a manual one stays until replaced.
        if (!IsAutoIsolation || IsolationTarget is null)
            return IsAutoSelect
                ? await ClearEffectsAsync(cancellationToken)
                : await ClearIsolationAsync(cancellationToken);

        var cleared = await _actions.SelectAsync([], cancellationToken, _progress);
        cancellationToken.ThrowIfCancellationRequested();

        if (cleared is not HostResult<bool>.Success {Value: true})
            return DescribeSelection(cleared, true);

        return await IsolateCurrentAsync(cancellationToken);
    }

    private async Task GoBackAsync(CancellationToken cancellationToken)
    {
        if (IsBrowsingBlockContents && Current is null)
        {
            await RestoreBlockParentAsync(cancellationToken);
            NotifyNavigation();
            return;
        }

        GoBackTo(_navigation.Path.Count - 1);
    }

    private async Task GoToRootAsync(CancellationToken cancellationToken)
    {
        await RestoreBlockParentAsync(cancellationToken);
        GoBackTo(0);
    }

    private async Task GoToBreadcrumbAsync(LensNode node, CancellationToken cancellationToken)
    {
        if (_blockParent is { } parent && parent.Navigation.Path.Contains(node))
        {
            var depth = Array.IndexOf([.. parent.Navigation.Path], node) + 1;
            await RestoreBlockParentAsync(cancellationToken);
            GoBackTo(depth);
            return;
        }

        GoBackTo(Array.IndexOf([.. _navigation.Path], node) + 1);
    }

    private async Task RestoreBlockParentAsync(CancellationToken cancellationToken)
    {
        if (_blockParent is not { } parent)
            return;

        var presentation = await BuildPresentationAsync(parent.Inventory, _grouping, false, cancellationToken);
        var groups = HasPropertyOptions(parent.Inventory)
            ? (await BuildPresentationAsync(parent.Inventory, _grouping, true, cancellationToken)).Groups
            : presentation.Groups;
        cancellationToken.ThrowIfCancellationRequested();
        ExitBlockContents();
        ApplyPresentation(presentation, groups);
    }

    private void ExitBlockContents()
    {
        if (_blockParent is not { } parent)
            return;

        _blockParent = null;
        _navigation = parent.Navigation;
        _searchText = parent.SearchText;
        OnPropertyChanged(nameof(SearchText));
    }

    private sealed record BlockParent(DrawingInventory Inventory, NavigationState Navigation, string SearchText);
}