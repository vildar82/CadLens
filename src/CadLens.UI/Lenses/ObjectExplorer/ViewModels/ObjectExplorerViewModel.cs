using System.Collections.Immutable;
using System.ComponentModel;
using CadLens.Lenses;
using Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CadLens.UI;

/// <summary>Drawing overview and asynchronous operations for the modeless panel.</summary>
public sealed class ObjectExplorerViewModel : ObservableObject, IDisposable
{
    private readonly IObjectExplorerActions _actions;
    private readonly DrawingGrouping _grouping;
    private readonly NavigationState _navigation = new();
    private ImmutableHashSet<string> _enabledFilters = [];
    private CancellationToken _activationToken;
    private string _emptyMessage = "Refresh to explore the active space.";
    private CancellationTokenSource? _pendingRequest;
    private Task _operation = Task.CompletedTask;
    private ImmutableArray<LensNode> _visibleGroups = [];
    private bool _sortDescending;
    private bool _disposed;
    private bool _needsCleanup;
    private bool _hasDrawing = true;
    private bool _spaceIsDrawingData;
    private UiMessage _status = new("Activate a lens to explore the drawing.");

    /// <summary>Creates toolkit commands for the injected host operations.</summary>
    /// <param name="actions">Context-checked host operations.</param>
    /// <param name="grouping">Root organization for this lens.</param>
    public ObjectExplorerViewModel(IObjectExplorerActions actions, DrawingGrouping grouping)
    {
        UiText.Current.PropertyChanged += OnLanguageChanged;
        _actions = actions;
        _grouping = grouping;
        LensLabel = grouping == DrawingGrouping.Layers ? "Layers" : "Object Types";
        RootLabel = grouping == DrawingGrouping.Layers ? "All layers" : "All types";
        SearchPlaceholder = grouping == DrawingGrouping.Layers ? "Search layers" : "Search types";
        GroupLabel = grouping == DrawingGrouping.Layers ? "layers" : "types";
        ReadCommand = new AsyncRelayCommand(() => ExecuteActionAsync(ReadInventoryAsync), CanRun);
        IsolateCommand = new AsyncRelayCommand(
            () => ExecuteActionAsync(async token => await _actions.IsolateObjectsAsync(Current!.Objects, token)),
            () => CanRun() && Current is {Objects.IsEmpty: false});
        ResetCommand = new AsyncRelayCommand(() => ExecuteActionAsync(ResetAsync), CanRun);
        SelectCommand = new AsyncRelayCommand(
            () => ExecuteActionAsync(SelectCurrentAsync),
            () => CanRun() && Current is {Objects.IsEmpty: false});
        FocusCommand = new AsyncRelayCommand(
            () => ExecuteActionAsync(async token => await _actions.FocusAsync(Current!.Objects, token)),
            () => CanRun() && HasFocusTarget());
        EnterCommand = new AsyncRelayCommand<LensNode>(
            node => NavigateAsync(() => Enter(node)),
            node => CanRun() && node is not null && Items.Contains(node));
        BackCommand = new AsyncRelayCommand(
            () => NavigateAsync(() => GoBackTo(_navigation.Path.Count - 1)),
            () => CanRun() && Current is not null);
        RootCommand = new AsyncRelayCommand(
            () => NavigateAsync(() => GoBackTo(0)),
            () => CanRun() && Current is not null);
        BreadcrumbCommand = new AsyncRelayCommand<LensNode>(
            node => NavigateAsync(() => GoBackTo(Array.IndexOf([.. _navigation.Path], node!) + 1)),
            node => CanRun() && node is not null && _navigation.Path.Contains(node));
        PreviousCommand = new AsyncRelayCommand(
            () => NavigateAsync(() => MoveObject(-1)),
            () => CanRun() && _navigation.CanPrevious);
        NextCommand = new AsyncRelayCommand(
            () => NavigateAsync(() => MoveObject(1)),
            () => CanRun() && _navigation.CanNext);
        ToggleFilterCommand = new AsyncRelayCommand<FilterOption>(
            filter => ExecuteActionAsync(token => ToggleFilterAsync(filter!, token)),
            filter => CanRun() && filter is not null && Filters.Contains(filter));
        ToggleAutoIsolationCommand = new AsyncRelayCommand(
            () => ExecuteActionAsync(ToggleAutoIsolationAsync),
            CanRun);
        ToggleAutoSelectCommand = new AsyncRelayCommand(
            () => ExecuteActionAsync(ToggleAutoSelectAsync),
            CanRun);
        ToggleAutoFocusCommand = new AsyncRelayCommand(
            () => ExecuteActionAsync(ToggleAutoFocusAsync),
            CanRun);
        SortByNameCommand = new RelayCommand(() => ChangeSort(false));
        SortByCountCommand = new RelayCommand(() => ChangeSort(true));
        ClearSearchCommand = new RelayCommand(() => SearchText = "");
    }

    /// <summary>Whether the lens is expanded and allowed to access the drawing.</summary>
    public bool IsLensActive
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
                NotifyCommands();
        }
    }

    /// <summary>Current operation result or explanation.</summary>
    public string Status => _status.ToString();

    /// <summary>Active space supplied by the lens.</summary>
    public string SpaceLabel
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
                OnPropertyChanged(nameof(DisplaySpaceLabel));
        }
    } = "Active drawing";

    /// <summary>Localized app status or the unchanged active drawing-space name.</summary>
    public string DisplaySpaceLabel => _spaceIsDrawingData ? SpaceLabel : UiText.Current.Get(SpaceLabel);

    /// <summary>Title supplied by the active lens.</summary>
    public string LensLabel
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>Breadcrumb label for this lens's root list.</summary>
    public string RootLabel
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>Root search hint supplied by the lens.</summary>
    public string SearchPlaceholder
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>Plural name of the root groups.</summary>
    public string GroupLabel
    {
        get;
        private set
        {
            if (!SetProperty(ref field, value))
                return;

            OnPropertyChanged(nameof(GroupSummary));
            OnPropertyChanged(nameof(EmptyMessage));
            OnPropertyChanged(nameof(SortByNameLabel));
            OnPropertyChanged(nameof(SortByCountLabel));
        }
    }

    /// <summary>Included root count and the lens-specific group name.</summary>
    public string GroupSummary => new UiMessage(
        _grouping == DrawingGrouping.Layers ? "{0:N0} layers" : "{0:N0} types",
        GroupCount).ToString();

    /// <summary>Accessible description of name sorting.</summary>
    public string SortByNameLabel => UiText.Current.Get(
        _grouping == DrawingGrouping.Layers ? "Sort layers by name" : "Sort types by name");

    /// <summary>Accessible description of count sorting.</summary>
    public string SortByCountLabel => UiText.Current.Get(
        _grouping == DrawingGrouping.Layers
            ? "Sort layers by object count"
            : "Sort types by object count");

    /// <summary>Groups from the most recent inventory.</summary>
    public ImmutableArray<LensNode> Groups
    {
        get;
        private set
        {
            SetProperty(ref field, value);
            UpdateVisibleGroups();
            OnPropertyChanged(nameof(GroupCount));
            OnPropertyChanged(nameof(GroupSummary));
            OnPropertyChanged(nameof(ObjectCount));
            OnPropertyChanged(nameof(HasGroups));
        }
    } = [];

    /// <summary>Children at the current exploration level.</summary>
    public ImmutableArray<LensNode> Items => Current is null ? _visibleGroups : _navigation.Items;

    /// <summary>Finds groups by name in the root list.</summary>
    public string SearchText
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
                UpdateVisibleGroups();
        }
    } = "";

    /// <summary>Whether the name column controls root ordering.</summary>
    public bool IsNameSortActive => !IsCountSortActive;

    /// <summary>Whether the object count column controls root ordering.</summary>
    public bool IsCountSortActive { get; private set; }

    /// <summary>Current direction indicator for the name column.</summary>
    public string NameSortArrow => !IsCountSortActive ? _sortDescending ? "↓" : "↑" : "";

    /// <summary>Current direction indicator for the object count column.</summary>
    public string CountSortArrow => IsCountSortActive ? _sortDescending ? "↓" : "↑" : "";

    /// <summary>Current group or object details.</summary>
    public LensNode? Current => _navigation.Current;

    /// <summary>Selected ancestors including the current node.</summary>
    public IReadOnlyList<LensNode> Breadcrumbs => _navigation.Path;

    /// <summary>Whether object browsing controls apply.</summary>
    public bool IsObject => _navigation.Position > 0;

    /// <summary>One-based position within the current object set.</summary>
    public string ObjectPosition =>
        new UiMessage("{0} of {1}", _navigation.Position, _navigation.ObjectCount).ToString();

    /// <summary>Whether details replace the root list.</summary>
    public bool HasCurrent => Current is not null;

    /// <summary>Fits the camera to the current target during navigation.</summary>
    public bool IsAutoFocus
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>Selects the current target during navigation without moving the camera.</summary>
    public bool IsAutoSelect
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>Isolates the current target during navigation.</summary>
    public bool IsAutoIsolation
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>Whether the current result has no root groups.</summary>
    public bool IsEmpty => Current is null && _visibleGroups.IsEmpty;

    /// <summary>Lens-provided explanation for an empty inventory.</summary>
    public string EmptyMessage => !Groups.IsEmpty && _visibleGroups.IsEmpty
        ? UiText.Current.Get(
            _grouping == DrawingGrouping.Layers
                ? "No layers match your search."
                : "No types match your search.")
        : UiText.Current.Get(_emptyMessage);

    /// <summary>Sorts root groups by name; clicking again reverses direction.</summary>
    public IRelayCommand SortByNameCommand { get; }

    /// <summary>Sorts root groups by object count; clicking again reverses direction.</summary>
    public IRelayCommand SortByCountCommand { get; }

    /// <summary>Clears the root group search.</summary>
    public IRelayCommand ClearSearchCommand { get; }

    /// <summary>Lens options and their current inclusion state.</summary>
    public ImmutableArray<FilterOption> Filters { get; private set; } = [];

    /// <summary>Enters a displayed child and updates temporary isolation.</summary>
    public IAsyncRelayCommand<LensNode> EnterCommand { get; }

    /// <summary>Returns to the parent level.</summary>
    public IAsyncRelayCommand BackCommand { get; }

    /// <summary>Returns to the root list.</summary>
    public IAsyncRelayCommand RootCommand { get; }

    /// <summary>Returns to the chosen ancestor.</summary>
    public IAsyncRelayCommand<LensNode> BreadcrumbCommand { get; }

    /// <summary>Shows the previous object.</summary>
    public IAsyncRelayCommand PreviousCommand { get; }

    /// <summary>Shows the next object.</summary>
    public IAsyncRelayCommand NextCommand { get; }

    /// <summary>Reloads inventory with one inclusion option toggled.</summary>
    public IAsyncRelayCommand<FilterOption> ToggleFilterCommand { get; }

    /// <summary>Switches navigation isolation on or off.</summary>
    public IAsyncRelayCommand ToggleAutoIsolationCommand { get; }

    /// <summary>Switches automatic CAD selection on or off.</summary>
    public IAsyncRelayCommand ToggleAutoSelectCommand { get; }

    /// <summary>Switches automatic camera focus on or off.</summary>
    public IAsyncRelayCommand ToggleAutoFocusCommand { get; }

    /// <summary>Selects the current targets without changing the camera or isolation.</summary>
    public IAsyncRelayCommand SelectCommand { get; }

    /// <summary>Number of included groups.</summary>
    public int GroupCount => Groups.Length;

    /// <summary>Number of included objects.</summary>
    public int ObjectCount => Groups.Sum(group => group.Count);

    /// <summary>Whether the inventory contains groups.</summary>
    public bool HasGroups => !Groups.IsEmpty;

    /// <summary>Whether a host operation is pending.</summary>
    public bool IsBusy => _pendingRequest is not null;

    /// <summary>Refreshes the active-space inventory.</summary>
    public IAsyncRelayCommand ReadCommand { get; }

    /// <summary>Isolates the current group or object.</summary>
    public IAsyncRelayCommand IsolateCommand { get; }

    /// <summary>Clears selection and isolation, turns off Auto modes, and preserves the camera.</summary>
    public IAsyncRelayCommand ResetCommand { get; }

    /// <summary>Explicitly fits the selected group's or object's live bounds.</summary>
    public IAsyncRelayCommand FocusCommand { get; }

    /// <summary>Clears old state and reloads the active lens after the document or space changes.</summary>
    /// <param name="hasDrawing">Whether drawing-dependent commands can run.</param>
    public Task ResetContextAsync(bool hasDrawing = true)
    {
        if (_disposed)
            return Task.CompletedTask;

        _hasDrawing = hasDrawing;
        _pendingRequest?.Cancel();
        Groups = [];
        _navigation.Reset([], false);

        NotifyNavigation();
        _spaceIsDrawingData = false;
        SpaceLabel = hasDrawing ? "Active drawing" : "No active drawing";
        OnPropertyChanged(nameof(DisplaySpaceLabel));
        SetStatus(
            !hasDrawing
                ? "Open a drawing to explore its objects."
                : IsLensActive
                    ? "Drawing context changed. Updating the current space."
                    : "Drawing context changed. Activate a lens to explore.");
        if (!_needsCleanup)
            return IsLensActive && hasDrawing ? RefreshContextAsync() : Task.CompletedTask;

        _actions.ClearImmediately(false);
        _needsCleanup = IsLensActive;

        return IsLensActive && hasDrawing ? RefreshContextAsync() : Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose() => Close(false);

    /// <summary>Cancels pending lens work and synchronously removes owned graphics.</summary>
    /// <param name="hostTerminating">Whether host shutdown forbids regeneration.</param>
    public void Close(bool hostTerminating)
    {
        if (_disposed)
            return;

        UiText.Current.PropertyChanged -= OnLanguageChanged;
        _disposed = true;
        IsLensActive = false;
        _pendingRequest?.Cancel();

        if (_needsCleanup)
            _actions.ClearImmediately(hostTerminating);
        NotifyCommands();
    }

    private bool CanRun() =>
        !_disposed && !_activationToken.IsCancellationRequested && IsLensActive && _hasDrawing && !IsBusy;

    private bool HasFocusTarget() =>
        Current is {Objects.IsEmpty: false} node && node.Actions.Contains(LensAction.Focus);

    private void ChangeSort(bool byCount)
    {
        _sortDescending = IsCountSortActive == byCount ? !_sortDescending : byCount;
        IsCountSortActive = byCount;
        OnPropertyChanged(nameof(IsNameSortActive));
        OnPropertyChanged(nameof(IsCountSortActive));
        OnPropertyChanged(nameof(NameSortArrow));
        OnPropertyChanged(nameof(CountSortArrow));
        UpdateVisibleGroups();
    }

    private void UpdateVisibleGroups()
    {
        var groups = Groups.Where(group => group.Label.Contains(
            SearchText.Trim(),
            StringComparison.OrdinalIgnoreCase));
        _visibleGroups =
        [
            .. IsCountSortActive
                ? _sortDescending
                    ? groups.OrderByDescending(group => group.Count)
                        .ThenBy(group => group.Label, StringComparer.OrdinalIgnoreCase)
                    : groups.OrderBy(group => group.Count)
                        .ThenBy(group => group.Label, StringComparer.OrdinalIgnoreCase)
                : _sortDescending
                    ? groups.OrderByDescending(group => group.Label, StringComparer.OrdinalIgnoreCase)
                    : groups.OrderBy(group => group.Label, StringComparer.OrdinalIgnoreCase)
        ];
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
        EnterCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Loads the lens view for the active session.</summary>
    /// <param name="cancellationToken">Canceled when the panel deactivates this lens.</param>
    public Task ActivateAsync(CancellationToken cancellationToken)
    {
        if (_disposed || !_hasDrawing || IsBusy)
            return Task.CompletedTask;

        _activationToken = cancellationToken;
        _needsCleanup = true;
        IsLensActive = true;
        return ExecuteActionAsync(ReadInventoryAsync);
    }

    /// <summary>Cancels drawing work and settles it before clearing lens effects.</summary>
    /// <param name="cancellationToken">Cleanup token independent of the canceled activation.</param>
    public async Task<HostResult<bool>> DeactivateAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
            return new HostResult<bool>.Unavailable($"The {LensLabel} session has closed.");

        IsLensActive = false;
        // ReSharper disable once MethodHasAsyncOverload -- Cancellation must finish before replacing request ownership.
        _pendingRequest?.Cancel();
        using var cleanup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pendingRequest = cleanup;
        NotifyBusy();

        try
        {
            await _operation;
            cleanup.Token.ThrowIfCancellationRequested();
            var result = await _actions.ClearAsync(cleanup.Token);
            cleanup.Token.ThrowIfCancellationRequested();
            SetStatus(DescribeCleanup(result));

            if (result is HostResult<bool>.Success {Value: true})
                _needsCleanup = false;

            return result;
        }
        catch (Exception exception)
        {
            if (!cleanup.IsCancellationRequested)
                SetStatus(new UiMessage("Cleanup did not complete: {0}", exception.Message));

            return new HostResult<bool>.Unavailable(exception.Message);
        }
        finally
        {
            _pendingRequest = null;
            NotifyBusy();
        }
    }

    private async Task RefreshContextAsync()
    {
        var previous = _operation;
        await previous;

        if (ReferenceEquals(previous, _operation))
            await ExecuteActionAsync(ReadInventoryAsync);
    }

    private async Task<UiMessage> ClearEffectsAsync(CancellationToken cancellationToken) =>
        DescribeCleanup(await _actions.ClearAsync(cancellationToken));

    private async Task<UiMessage> ResetAsync(CancellationToken cancellationToken)
    {
        IsAutoFocus = false;
        IsAutoSelect = false;
        IsAutoIsolation = false;

        var result = await _actions.ClearAsync(cancellationToken);
        return result.Match(
            cleared => new UiMessage(
                cleared
                    ? "Selection and isolation cleared. Auto modes off."
                    : "Cleanup did not complete."),
            reason => new UiMessage("Cleanup unavailable: {0}", new UiMessage(reason)));
    }

    private static UiMessage DescribeCleanup(HostResult<bool> result) => result.Match(
        cleared => new UiMessage(
            cleared
                ? "Selection and isolation cleared. Auto settings kept."
                : "Cleanup did not complete."),
        reason => new UiMessage("Cleanup unavailable: {0}", new UiMessage(reason)));

    private void NotifyCommands()
    {
        ReadCommand.NotifyCanExecuteChanged();
        IsolateCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
        SelectCommand.NotifyCanExecuteChanged();
        FocusCommand.NotifyCanExecuteChanged();
        EnterCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
        RootCommand.NotifyCanExecuteChanged();
        BreadcrumbCommand.NotifyCanExecuteChanged();
        PreviousCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
        ToggleFilterCommand.NotifyCanExecuteChanged();
        ToggleAutoIsolationCommand.NotifyCanExecuteChanged();
        ToggleAutoSelectCommand.NotifyCanExecuteChanged();
        ToggleAutoFocusCommand.NotifyCanExecuteChanged();
    }

    private async Task<UiMessage> ReadInventoryAsync(CancellationToken cancellationToken)
    {
        var cleared = await _actions.ClearAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (cleared is not HostResult<bool>.Success {Value: true})
            return DescribeCleanup(cleared);

        var result = await _actions.ReadAsync(_grouping, _enabledFilters, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (result is not HostResult<LensPresentation>.Success success)
        {
            Groups = [];
            _navigation.Reset([], false);
            NotifyNavigation();
            _spaceIsDrawingData = false;
            SpaceLabel = "Unavailable";
            OnPropertyChanged(nameof(DisplaySpaceLabel));
            return ((HostResult<LensPresentation>.Unavailable) result).Reason;
        }

        _spaceIsDrawingData = true;
        SpaceLabel = success.Value.SpaceLabel;
        OnPropertyChanged(nameof(DisplaySpaceLabel));
        LensLabel = success.Value.Label;
        RootLabel = success.Value.RootLabel;
        SearchPlaceholder = success.Value.SearchPlaceholder;
        GroupLabel = success.Value.GroupLabel;
        Groups = success.Value.Groups;
        _emptyMessage = success.Value.EmptyMessage;
        Filters =
        [
            .. success.Value.Filters.Select(filter => new FilterOption(filter, _enabledFilters.Contains(filter.Id)))
        ];
        _navigation.Reset(Groups, true);
        OnPropertyChanged(nameof(Filters));
        OnPropertyChanged(nameof(EmptyMessage));
        NotifyNavigation();

        if (Current is not null)
            return await ApplySelectionAndIsolationAsync(cancellationToken);

        return HasGroups
            ? _grouping == DrawingGrouping.Layers
                ? "Choose a layer. Auto modes apply while browsing."
                : "Choose a type. Auto modes apply while browsing."
            : success.Value.EmptyMessage;
    }

    private async Task<UiMessage> ToggleAutoIsolationAsync(CancellationToken cancellationToken)
    {
        IsAutoIsolation = !IsAutoIsolation;

        if (IsAutoIsolation && Current is not null)
            return await _actions.IsolateObjectsAsync(Current.Objects, cancellationToken);

        return await ClearIsolationAsync(cancellationToken);
    }

    private async Task<UiMessage> ToggleAutoSelectAsync(CancellationToken cancellationToken)
    {
        IsAutoSelect = !IsAutoSelect;

        return IsAutoSelect && Current is not null
            ? await SelectCurrentAsync(cancellationToken)
            : DescribeSelection(await _actions.SelectAsync([], cancellationToken), true);
    }

    private async Task<UiMessage> ToggleAutoFocusAsync(CancellationToken cancellationToken)
    {
        IsAutoFocus = !IsAutoFocus;

        if (IsAutoFocus && Current is not null)
            return await FocusCurrentAsync(cancellationToken);

        return IsAutoFocus ? "Auto focus on." : "Auto focus off. Camera kept.";
    }

    private async Task<UiMessage> SelectCurrentAsync(CancellationToken cancellationToken) =>
        DescribeSelection(await _actions.SelectAsync(Current!.Objects, cancellationToken), false);

    private static UiMessage DescribeSelection(HostResult<bool> result, bool clearing)
    {
        if (result is HostResult<bool>.Unavailable unavailable)
            return new UiMessage("Selection unavailable: {0}", new UiMessage(unavailable.Reason));

        if (result is not HostResult<bool>.Success {Value: true})
            return "Selection did not complete.";

        return clearing ? "CAD selection cleared." : "CAD objects selected.";
    }

    private async Task<UiMessage> ClearIsolationAsync(CancellationToken cancellationToken) =>
        (await _actions.ClearIsolationAsync(cancellationToken)).Match(
            cleared => new UiMessage(cleared ? "Temporary isolation cleared." : "Isolation cleanup did not complete."),
            reason => new UiMessage("Isolation cleanup unavailable: {0}", new UiMessage(reason)));

    private async Task<UiMessage> FocusCurrentAsync(CancellationToken cancellationToken) => HasFocusTarget()
        ? await _actions.FocusAsync(Current!.Objects, cancellationToken)
        : "Focus unavailable: the current target has no usable bounds.";

    private async Task<UiMessage> ApplySelectionAndIsolationAsync(CancellationToken cancellationToken)
    {
        List<UiMessage> messages = [];

        if (IsAutoSelect)
            messages.Add(await SelectCurrentAsync(cancellationToken));

        cancellationToken.ThrowIfCancellationRequested();
        messages.Add(
            IsAutoIsolation
                ? await _actions.IsolateObjectsAsync(Current!.Objects, cancellationToken)
                : await ClearIsolationAsync(cancellationToken));

        return messages.Count == 1 ? messages[0] : new UiMessage("{0} {1}", messages[0], messages[1]);
    }

    private Task NavigateAsync(Action navigate) => ExecuteActionAsync(async token =>
    {
        navigate();
        if (Current is null)
            return await ClearEffectsAsync(token);

        var message = await ApplySelectionAndIsolationAsync(token);

        if (!IsAutoFocus)
            return message;

        token.ThrowIfCancellationRequested();
        return new UiMessage("{0} {1}", await FocusCurrentAsync(token), message);
    });

    private void Enter(LensNode? node)
    {
        if (node is not null && _navigation.Enter(node.Id))
            NotifyNavigation();
    }

    private void GoBackTo(int depth)
    {
        _navigation.GoBackTo(depth);
        NotifyNavigation();
    }

    private void MoveObject(int offset)
    {
        _navigation.MoveObject(offset);
        NotifyNavigation();
    }

    private async Task<UiMessage> ToggleFilterAsync(FilterOption filter, CancellationToken cancellationToken)
    {
        _enabledFilters = filter.IsEnabled
            ? _enabledFilters.Remove(filter.Descriptor.Id)
            : _enabledFilters.Add(filter.Descriptor.Id);
        Filters =
        [
            .. Filters.Select(option => option with {IsEnabled = _enabledFilters.Contains(option.Descriptor.Id)})
        ];
        OnPropertyChanged(nameof(Filters));

        try
        {
            return await ReadInventoryAsync(cancellationToken);
        }
        catch
        {
            if (cancellationToken.IsCancellationRequested)
                throw;

            Groups = [];
            _navigation.Reset([], false);
            NotifyNavigation();

            throw;
        }
    }

    private void NotifyNavigation()
    {
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(Breadcrumbs));
        OnPropertyChanged(nameof(HasCurrent));
        OnPropertyChanged(nameof(IsObject));
        OnPropertyChanged(nameof(ObjectPosition));
        OnPropertyChanged(nameof(IsEmpty));
        NotifyCommands();
    }

    private Task ExecuteActionAsync(Func<CancellationToken, Task<UiMessage>> action)
    {
        if (!CanRun())
            return Task.CompletedTask;

        _operation = CompleteActionAsync(action);
        return _operation;
    }

    private async Task CompleteActionAsync(Func<CancellationToken, Task<UiMessage>> action)
    {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_activationToken);
        _pendingRequest = request;
        NotifyBusy();

        try
        {
            SetStatus("Working…");
            var message = await action(request.Token);
            request.Token.ThrowIfCancellationRequested();
            SetStatus(message);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            // Collapse, close, or a context change supplies the next visible state.
        }
        catch (Exception exception)
        {
            if (!request.IsCancellationRequested)
                SetStatus(new UiMessage("Unable to complete the operation: {0}", exception.Message));
        }
        finally
        {
            if (ReferenceEquals(_pendingRequest, request))
            {
                _pendingRequest = null;
                NotifyBusy();
            }
        }
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs args) => OnPropertyChanged(string.Empty);

    private void SetStatus(UiMessage message)
    {
        _status = message;
        OnPropertyChanged(nameof(Status));
    }

    private void NotifyBusy()
    {
        OnPropertyChanged(nameof(IsBusy));
        NotifyCommands();
    }
}