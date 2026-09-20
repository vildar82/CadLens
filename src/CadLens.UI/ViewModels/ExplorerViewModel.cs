using System.Collections.Immutable;
using CadLens.Core;
using Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CadLens.UI;

/// <summary>Drawing overview and asynchronous operations for the modeless panel.</summary>
public sealed class ExplorerViewModel : ObservableObject, IDisposable
{
    private readonly IExplorerActions _actions;
    private readonly NavigationState _navigation = new();
    private ImmutableHashSet<string> _enabledFilters = [];
    private ImmutableArray<FilterOption> _filters = [];
    private string _emptyMessage = "Refresh to explore the active space.";
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _pendingRequest;
    private string _status = "Refresh, then open a group to highlight its objects.";
    private string _spaceLabel = "Active drawing";
    private string _lensLabel = "Overview";
    private string? _lensId;
    private ImmutableArray<LensNode> _groups = [];
    private bool _isBusy;
    private bool _disposed;
    private bool _hasDrawing = true;
    private int _contextVersion;

    /// <summary>Creates toolkit commands for the injected host operations.</summary>
    /// <param name="actions">Context-checked host operations.</param>
    public ExplorerViewModel(IExplorerActions actions)
    {
        _actions = actions;
        ReadCommand = new AsyncRelayCommand(() => RunAsync(ReadInventoryAsync), CanRun);
        EmphasizeCommand = new AsyncRelayCommand(() => RunAsync(_actions.EmphasizeAsync), CanRun);
        ClearCommand = new AsyncRelayCommand(() => RunAsync(_actions.ClearAsync), CanRun);
        FocusCommand = new AsyncRelayCommand(
            () => RunAsync(token => _actions.FocusAsync(Current!.Objects, token)),
            () => CanRun() && Current is { Objects.IsEmpty: false } node && node.Actions.Contains(LensAction.Focus));
        EnterCommand = new AsyncRelayCommand<LensNode>(node => NavigateAsync(() => Enter(node)), node => CanRun() && node is not null && Items.Contains(node));
        BackCommand = new AsyncRelayCommand(() => NavigateAsync(() => GoBackTo(_navigation.Path.Count - 1)), () => CanRun() && Current is not null);
        RootCommand = new AsyncRelayCommand(() => NavigateAsync(() => GoBackTo(0)), () => CanRun() && Current is not null);
        BreadcrumbCommand = new AsyncRelayCommand<LensNode>(
            node => NavigateAsync(() => GoBackTo(_navigation.Path.ToList().IndexOf(node!) + 1)),
            node => CanRun() && node is not null && _navigation.Path.Contains(node));
        PreviousCommand = new AsyncRelayCommand(() => NavigateAsync(() => MoveObject(-1)), () => CanRun() && _navigation.CanPrevious);
        NextCommand = new AsyncRelayCommand(() => NavigateAsync(() => MoveObject(1)), () => CanRun() && _navigation.CanNext);
        ToggleFilterCommand = new AsyncRelayCommand<FilterOption>(
            filter => RunAsync(token => ToggleFilterAsync(filter!, token)),
            filter => CanRun() && filter is not null && Filters.Contains(filter));
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

    /// <summary>Whether the current result has no root groups.</summary>
    public bool IsEmpty => Groups.IsEmpty;

    /// <summary>Lens-provided explanation for an empty inventory.</summary>
    public string EmptyMessage => _emptyMessage;

    /// <summary>Lens options and their current inclusion state.</summary>
    public ImmutableArray<FilterOption> Filters => _filters;

    /// <summary>Enters a displayed child and updates temporary emphasis without moving the view.</summary>
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

    /// <summary>Number of included groups.</summary>
    public int GroupCount => Groups.Length;

    /// <summary>Number of included objects.</summary>
    public int ObjectCount => Groups.Sum(group => group.Count);

    /// <summary>Whether the inventory contains groups.</summary>
    public bool HasGroups => !Groups.IsEmpty;

    /// <summary>Whether a host operation is pending.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
                return;

            NotifyCommands();
        }
    }

    /// <summary>Refreshes the active-space inventory.</summary>
    public IAsyncRelayCommand ReadCommand { get; }

    /// <summary>Highlights the drawing selection.</summary>
    public IAsyncRelayCommand EmphasizeCommand { get; }

    /// <summary>Removes temporary rendering.</summary>
    public IAsyncRelayCommand ClearCommand { get; }

    /// <summary>Explicitly fits the selected group's or object's live bounds.</summary>
    public IAsyncRelayCommand FocusCommand { get; }

    /// <summary>Clears presentation when the document or space changes.</summary>
    /// <param name="hasDrawing">Whether drawing-dependent commands can run.</param>
    public void ResetContext(bool hasDrawing = true)
    {
        _contextVersion++;
        _hasDrawing = hasDrawing;
        _pendingRequest?.Cancel();
        Groups = [];
        _navigation.Reset([], false);
        NotifyNavigation();
        SpaceLabel = hasDrawing ? "Active drawing" : "No active drawing";
        Status = hasDrawing ? "Drawing context changed. Updating the current space." : "Open a drawing to explore its objects.";
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        NotifyCommands();
    }

    private bool CanRun() => !_disposed && _hasDrawing && !IsBusy;

    private void NotifyCommands()
    {
        ReadCommand.NotifyCanExecuteChanged();
        EmphasizeCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
        FocusCommand.NotifyCanExecuteChanged();
        EnterCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
        RootCommand.NotifyCanExecuteChanged();
        BreadcrumbCommand.NotifyCanExecuteChanged();
        PreviousCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
        ToggleFilterCommand.NotifyCanExecuteChanged();
    }

    private async Task<string> ReadInventoryAsync(CancellationToken cancellationToken)
    {
        var version = _contextVersion;
        await _actions.EmphasizeObjectsAsync([], cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (_disposed || version != _contextVersion)
            return string.Empty;

        var result = await _actions.ReadAsync(_enabledFilters, cancellationToken);

        if (_disposed || version != _contextVersion)
            return string.Empty;

        if (result is not HostResult<LensPresentation>.Success success)
        {
            Groups = [];
            _navigation.Reset([], false);
            NotifyNavigation();
            SpaceLabel = "Unavailable";
            return ((HostResult<LensPresentation>.Unavailable)result).Reason;
        }

        SpaceLabel = success.Value.SpaceLabel;
        LensLabel = success.Value.Label;
        Groups = success.Value.Groups;
        _emptyMessage = success.Value.EmptyMessage;
        _filters = success.Value.Filters.Select(filter => new FilterOption(filter, _enabledFilters.Contains(filter.Id))).ToImmutableArray();
        _navigation.Reset(Groups, _lensId == success.Value.LensId);
        _lensId = success.Value.LensId;
        OnPropertyChanged(nameof(Filters));
        OnPropertyChanged(nameof(EmptyMessage));
        NotifyNavigation();

        if (Current is not null)
            return await _actions.EmphasizeObjectsAsync(Current.Objects, cancellationToken);

        return HasGroups ? "Open a group to highlight its objects." : success.Value.EmptyMessage;
    }

    private Task NavigateAsync(Action navigate) => RunAsync(token =>
    {
        navigate();
        return _actions.EmphasizeObjectsAsync(Current?.Objects ?? [], token);
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

    private async Task RunAsync(Func<CancellationToken, Task<string>> action)
    {
        if (!CanRun())
            return;

        var version = _contextVersion;
        IsBusy = true;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _pendingRequest = request;

        try
        {
            Status = "Working… Finish any active AutoCAD command to continue.";
            var message = await action(request.Token);

            if (!_disposed && version == _contextVersion)
                Status = message;
        }
        catch (OperationCanceledException)
        {
            if (!_disposed && version == _contextVersion)
                Status = "The request was cancelled.";
        }
        catch (Exception exception)
        {
            if (!_disposed && version == _contextVersion)
                Status = $"Unable to complete the operation: {exception.Message}";
        }
        finally
        {
            _pendingRequest = null;
            IsBusy = false;
        }
    }
}