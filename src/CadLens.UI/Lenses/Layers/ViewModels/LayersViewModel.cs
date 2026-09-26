using CadLens.Lenses;
using System.Collections.Immutable;
using Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CadLens.UI;

/// <summary>Drawing overview and asynchronous operations for the modeless panel.</summary>
public sealed class LayersViewModel : ObservableObject, IDisposable
{
    private readonly ILayersActions _actions;
    private readonly NavigationState _navigation = new();
    private ImmutableHashSet<string> _enabledFilters = [];
    private CancellationToken _activationToken;
    private bool _isLensActive;
    private ImmutableArray<FilterOption> _filters = [];
    private string _emptyMessage = "Refresh to explore the active space.";
    private CancellationTokenSource? _pendingRequest;
    private Task _operation = Task.CompletedTask;
    private string _status = "Activate a lens to explore the drawing.";
    private string _spaceLabel = "Active drawing";
    private string _lensLabel = "Overview";
    private ImmutableArray<LensNode> _groups = [];
    private bool _disposed;
    private bool _needsCleanup;
    private bool _hasDrawing = true;
    private bool _isAutoFocus;
    private bool _isAutoSelect;
    private bool _isAutoHighlight = true;

    /// <summary>Creates toolkit commands for the injected host operations.</summary>
    /// <param name="actions">Context-checked host operations.</param>
    public LayersViewModel(ILayersActions actions)
    {
        _actions = actions;
        ReadCommand = new AsyncRelayCommand(() => ExecuteActionAsync(ReadInventoryAsync), CanRun);
        HighlightCommand = new AsyncRelayCommand(
            () => ExecuteActionAsync(token => _actions.EmphasizeObjectsAsync(Current!.Objects, token)),
            () => CanRun() && Current is { Objects.IsEmpty: false });
        ResetCommand = new AsyncRelayCommand(() => ExecuteActionAsync(ClearEffectsAsync), CanRun);
        SelectCommand = new AsyncRelayCommand(
            () => ExecuteActionAsync(SelectCurrentAsync),
            () => CanRun() && Current is { Objects.IsEmpty: false });
        FocusCommand = new AsyncRelayCommand(
            () => ExecuteActionAsync(token => _actions.FocusAsync(Current!.Objects, token)),
            () => CanRun() && HasFocusTarget());
        EnterCommand = new AsyncRelayCommand<LensNode>(node => NavigateAsync(() => Enter(node)), node => CanRun() && node is not null && Items.Contains(node));
        BackCommand = new AsyncRelayCommand(() => NavigateAsync(() => GoBackTo(_navigation.Path.Count - 1)), () => CanRun() && Current is not null);
        RootCommand = new AsyncRelayCommand(() => NavigateAsync(() => GoBackTo(0)), () => CanRun() && Current is not null);
        BreadcrumbCommand = new AsyncRelayCommand<LensNode>(
            node => NavigateAsync(() => GoBackTo(_navigation.Path.ToList().IndexOf(node!) + 1)),
            node => CanRun() && node is not null && _navigation.Path.Contains(node));
        PreviousCommand = new AsyncRelayCommand(() => NavigateAsync(() => MoveObject(-1)), () => CanRun() && _navigation.CanPrevious);
        NextCommand = new AsyncRelayCommand(() => NavigateAsync(() => MoveObject(1)), () => CanRun() && _navigation.CanNext);
        ToggleFilterCommand = new AsyncRelayCommand<FilterOption>(
            filter => ExecuteActionAsync(token => ToggleFilterAsync(filter!, token)),
            filter => CanRun() && filter is not null && Filters.Contains(filter));
        ToggleAutoHighlightCommand = new AsyncRelayCommand(
            () => ExecuteActionAsync(ToggleAutoHighlightAsync), CanRun);
        ToggleAutoSelectCommand = new AsyncRelayCommand(
            () => ExecuteActionAsync(ToggleAutoSelectAsync), CanRun);
        ToggleAutoFocusCommand = new AsyncRelayCommand(
            () => ExecuteActionAsync(ToggleAutoFocusAsync), CanRun);
    }

    /// <summary>Whether the lens is expanded and allowed to access the drawing.</summary>
    public bool IsLensActive
    {
        get => _isLensActive;
        private set
        {
            if (SetProperty(ref _isLensActive, value))
                NotifyCommands();
        }
    }

    /// <summary>Current operation result or explanation.</summary>
    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>Active space supplied by the lens.</summary>
    public string SpaceLabel
    {
        get => _spaceLabel;
        private set => SetProperty(ref _spaceLabel, value);
    }

    /// <summary>Title supplied by the active lens.</summary>
    public string LensLabel
    {
        get => _lensLabel;
        private set => SetProperty(ref _lensLabel, value);
    }

    /// <summary>Groups from the most recent inventory.</summary>
    public ImmutableArray<LensNode> Groups
    {
        get => _groups;
        private set
        {
            SetProperty(ref _groups, value);
            OnPropertyChanged(nameof(GroupCount));
            OnPropertyChanged(nameof(ObjectCount));
            OnPropertyChanged(nameof(HasGroups));
        }
    }

    /// <summary>Children at the current exploration level.</summary>
    public ImmutableArray<LensNode> Items => _navigation.Items;

    /// <summary>Current group or object details.</summary>
    public LensNode? Current => _navigation.Current;

    /// <summary>Selected ancestors including the current node.</summary>
    public IReadOnlyList<LensNode> Breadcrumbs => _navigation.Path;

    /// <summary>Whether object browsing controls apply.</summary>
    public bool IsObject => _navigation.Position > 0;

    /// <summary>One-based position within the current object set.</summary>
    public string ObjectPosition => $"{_navigation.Position} of {_navigation.ObjectCount}";

    /// <summary>Whether details replace the root list.</summary>
    public bool HasCurrent => Current is not null;

    /// <summary>Fits the camera to the current target during navigation.</summary>
    public bool IsAutoFocus
    {
        get => _isAutoFocus;
        private set => SetProperty(ref _isAutoFocus, value);
    }

    /// <summary>Selects the current target during navigation without moving the camera.</summary>
    public bool IsAutoSelect
    {
        get => _isAutoSelect;
        private set => SetProperty(ref _isAutoSelect, value);
    }

    /// <summary>Highlights the current target during navigation.</summary>
    public bool IsAutoHighlight
    {
        get => _isAutoHighlight;
        private set => SetProperty(ref _isAutoHighlight, value);
    }

    /// <summary>Whether the current result has no root groups.</summary>
    public bool IsEmpty => Groups.IsEmpty;

    /// <summary>Lens-provided explanation for an empty inventory.</summary>
    public string EmptyMessage => _emptyMessage;

    /// <summary>Lens options and their current inclusion state.</summary>
    public ImmutableArray<FilterOption> Filters => _filters;

    /// <summary>Enters a displayed child and updates temporary emphasis.</summary>
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

    /// <summary>Switches navigation highlighting on or off.</summary>
    public IAsyncRelayCommand ToggleAutoHighlightCommand { get; }

    /// <summary>Switches automatic CAD selection on or off.</summary>
    public IAsyncRelayCommand ToggleAutoSelectCommand { get; }

    /// <summary>Switches automatic camera focus on or off.</summary>
    public IAsyncRelayCommand ToggleAutoFocusCommand { get; }

    /// <summary>Selects the current targets without changing the camera or highlighting.</summary>
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

    /// <summary>Highlights the current group or object.</summary>
    public IAsyncRelayCommand HighlightCommand { get; }

    /// <summary>Clears selection and highlighting while preserving Auto settings and the camera.</summary>
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
        SpaceLabel = hasDrawing ? "Active drawing" : "No active drawing";
        Status = !hasDrawing
            ? "Open a drawing to explore its objects."
            : IsLensActive ? "Drawing context changed. Updating the current space." : "Drawing context changed. Activate a lens to explore.";
        if (_needsCleanup)
        {
            _actions.ClearImmediately(false);
            _needsCleanup = IsLensActive;
        }

        return IsLensActive && hasDrawing ? RefreshContextAsync() : Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose() => Close(false);

    /// <summary>Cancels pending Layers work and synchronously removes owned graphics.</summary>
    /// <param name="hostTerminating">Whether host shutdown forbids regeneration.</param>
    public void Close(bool hostTerminating)
    {
        if (_disposed)
            return;

        _disposed = true;
        IsLensActive = false;
        _pendingRequest?.Cancel();

        if (_needsCleanup)
            _actions.ClearImmediately(hostTerminating);
        NotifyCommands();
    }

    private bool CanRun() => !_disposed && !_activationToken.IsCancellationRequested && IsLensActive && _hasDrawing && !IsBusy;

    private bool HasFocusTarget() => Current is { Objects.IsEmpty: false } node && node.Actions.Contains(LensAction.Focus);

    /// <summary>Loads the Layers view for the active session.</summary>
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

    /// <summary>Cancels drawing work and settles it before clearing Layers effects.</summary>
    /// <param name="cancellationToken">Cleanup token independent of the canceled activation.</param>
    public async Task<HostResult<bool>> DeactivateAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
            return new HostResult<bool>.Unavailable("The Layers session has closed.");

        IsLensActive = false;
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
            Status = DescribeCleanup(result);

            if (result is HostResult<bool>.Success { Value: true })
                _needsCleanup = false;

            return result;
        }
        catch (Exception exception)
        {
            if (!cleanup.IsCancellationRequested)
                Status = $"Cleanup did not complete: {exception.Message}";

            return new HostResult<bool>.Unavailable(exception.Message);
        }
        finally
        {
            _pendingRequest = null;
            NotifyBusy();
        }
    }

    /// <summary>Invites an explicit refresh without scheduling more drawing work.</summary>
    public void OnDrawingChanged()
    {
        if (CanRun())
            Status = "Drawing changed. Refresh to update the layer list.";
    }

    private async Task RefreshContextAsync()
    {
        var previous = _operation;
        await previous;

        if (ReferenceEquals(previous, _operation))
            await ExecuteActionAsync(ReadInventoryAsync);
    }

    private async Task<string> ClearEffectsAsync(CancellationToken cancellationToken) =>
        DescribeCleanup(await _actions.ClearAsync(cancellationToken));

    private static string DescribeCleanup(HostResult<bool> result) => result.Match(
        cleared => cleared ? "Selection and highlight cleared. Auto settings kept." : "Cleanup did not complete.",
        reason => $"Cleanup unavailable: {reason}");

    private void NotifyCommands()
    {
        ReadCommand.NotifyCanExecuteChanged();
        HighlightCommand.NotifyCanExecuteChanged();
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
        ToggleAutoHighlightCommand.NotifyCanExecuteChanged();
        ToggleAutoSelectCommand.NotifyCanExecuteChanged();
        ToggleAutoFocusCommand.NotifyCanExecuteChanged();
    }

    private async Task<string> ReadInventoryAsync(CancellationToken cancellationToken)
    {
        var cleared = await _actions.ClearAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (cleared is not HostResult<bool>.Success { Value: true })
            return DescribeCleanup(cleared);

        var result = await _actions.ReadAsync(_enabledFilters, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (result is not HostResult<LayersPresentation>.Success success)
        {
            Groups = [];
            _navigation.Reset([], false);
            NotifyNavigation();
            SpaceLabel = "Unavailable";
            return ((HostResult<LayersPresentation>.Unavailable)result).Reason;
        }

        SpaceLabel = success.Value.SpaceLabel;
        LensLabel = success.Value.Label;
        Groups = success.Value.Groups;
        _emptyMessage = success.Value.EmptyMessage;
        _filters = success.Value.Filters.Select(filter => new FilterOption(filter, _enabledFilters.Contains(filter.Id))).ToImmutableArray();
        _navigation.Reset(Groups, true);
        OnPropertyChanged(nameof(Filters));
        OnPropertyChanged(nameof(EmptyMessage));
        NotifyNavigation();

        if (Current is not null)
            return await ApplySelectionAndHighlightAsync(cancellationToken);

        return HasGroups ? "Choose a layer. Auto modes apply while browsing." : success.Value.EmptyMessage;
    }

    private async Task<string> ToggleAutoHighlightAsync(CancellationToken cancellationToken)
    {
        IsAutoHighlight = !IsAutoHighlight;

        if (IsAutoHighlight && Current is not null)
            return await _actions.EmphasizeObjectsAsync(Current.Objects, cancellationToken);

        return await ClearHighlightAsync(cancellationToken);
    }

    private async Task<string> ToggleAutoSelectAsync(CancellationToken cancellationToken)
    {
        IsAutoSelect = !IsAutoSelect;

        return IsAutoSelect && Current is not null
            ? await SelectCurrentAsync(cancellationToken)
            : DescribeSelection(await _actions.SelectAsync([], cancellationToken), true);
    }

    private async Task<string> ToggleAutoFocusAsync(CancellationToken cancellationToken)
    {
        IsAutoFocus = !IsAutoFocus;

        if (IsAutoFocus && Current is not null)
            return await FocusCurrentAsync(cancellationToken);

        return IsAutoFocus ? "Auto focus on." : "Auto focus off. Camera kept.";
    }

    private async Task<string> SelectCurrentAsync(CancellationToken cancellationToken) =>
        DescribeSelection(await _actions.SelectAsync(Current!.Objects, cancellationToken), false);

    private static string DescribeSelection(HostResult<bool> result, bool clearing)
    {
        if (result is HostResult<bool>.Unavailable unavailable)
            return $"Selection unavailable: {unavailable.Reason}";

        if (result is not HostResult<bool>.Success { Value: true })
            return "Selection did not complete.";

        return clearing ? "CAD selection cleared." : "CAD objects selected.";
    }

    private async Task<string> ClearHighlightAsync(CancellationToken cancellationToken) =>
        (await _actions.ClearHighlightAsync(cancellationToken)).Match(
            cleared => cleared ? "Temporary highlight cleared." : "Highlight cleanup did not complete.",
            reason => $"Highlight cleanup unavailable: {reason}");

    private Task<string> FocusCurrentAsync(CancellationToken cancellationToken) => HasFocusTarget()
        ? _actions.FocusAsync(Current!.Objects, cancellationToken)
        : Task.FromResult("Focus unavailable: the current target has no usable bounds.");

    private async Task<string> ApplySelectionAndHighlightAsync(CancellationToken cancellationToken)
    {
        var messages = new List<string>();

        if (IsAutoSelect)
            messages.Add(await SelectCurrentAsync(cancellationToken));

        cancellationToken.ThrowIfCancellationRequested();
        messages.Add(IsAutoHighlight
            ? await _actions.EmphasizeObjectsAsync(Current!.Objects, cancellationToken)
            : await ClearHighlightAsync(cancellationToken));

        return string.Join(" ", messages);
    }

    private Task NavigateAsync(Action navigate) => ExecuteActionAsync(async token =>
    {
        navigate();
        if (Current is null)
            return await ClearEffectsAsync(token);

        var message = await ApplySelectionAndHighlightAsync(token);

        if (!IsAutoFocus)
            return message;

        token.ThrowIfCancellationRequested();
        return $"{await FocusCurrentAsync(token)} {message}";
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

    private async Task<string> ToggleFilterAsync(FilterOption filter, CancellationToken cancellationToken)
    {
        _enabledFilters = filter.IsEnabled ? _enabledFilters.Remove(filter.Descriptor.Id) : _enabledFilters.Add(filter.Descriptor.Id);
        _filters = Filters.Select(option => option with { IsEnabled = _enabledFilters.Contains(option.Descriptor.Id) }).ToImmutableArray();
        OnPropertyChanged(nameof(Filters));

        try
        {
            return await ReadInventoryAsync(cancellationToken);
        }
        catch
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                Groups = [];
                _navigation.Reset([], false);
                NotifyNavigation();
            }

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

    private Task ExecuteActionAsync(Func<CancellationToken, Task<string>> action)
    {
        if (!CanRun())
            return Task.CompletedTask;

        _operation = CompleteActionAsync(action);
        return _operation;
    }

    private async Task CompleteActionAsync(Func<CancellationToken, Task<string>> action)
    {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_activationToken);
        _pendingRequest = request;
        NotifyBusy();

        try
        {
            Status = "Working…";
            var message = await action(request.Token);
            request.Token.ThrowIfCancellationRequested();
            Status = message;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            // Collapse, close, or a context change supplies the next visible state.
        }
        catch (Exception exception)
        {
            if (!request.IsCancellationRequested)
                Status = $"Unable to complete the operation: {exception.Message}";
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

    private void NotifyBusy()
    {
        OnPropertyChanged(nameof(IsBusy));
        NotifyCommands();
    }
}
