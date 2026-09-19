using System.Collections.Immutable;
using CadLens.Core;
using Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CadLens.UI;

/// <summary>Drawing overview and asynchronous operations for the modeless panel.</summary>
public sealed class VerificationViewModel : ObservableObject, IDisposable
{
    private readonly IVerificationActions _actions;
    private readonly CancellationTokenSource _lifetime = new();
    private string _status = "Select objects in your drawing, then highlight them here.";
    private string _spaceLabel = "Active drawing";
    private string _lensLabel = "Overview";
    private ImmutableArray<LensNode> _groups = [];
    private bool _isBusy;
    private bool _disposed;
    private int _contextVersion;

    /// <summary>Creates toolkit commands for the injected host operations.</summary>
    /// <param name="actions">Context-checked host operations.</param>
    public VerificationViewModel(IVerificationActions actions)
    {
        _actions = actions;
        ReadCommand = new AsyncRelayCommand(() => RunAsync(ReadInventoryAsync), CanRun);
        EmphasizeCommand = new AsyncRelayCommand(() => RunAsync(_actions.EmphasizeAsync), CanRun);
        ClearCommand = new AsyncRelayCommand(() => RunAsync(_actions.ClearAsync), CanRun);
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

    /// <summary>Clears presentation when the document or space changes.</summary>
    public void ResetContext()
    {
        _contextVersion++;
        Groups = [];
        SpaceLabel = "Active drawing";
        Status = "Drawing context changed. Refresh to read the current space.";
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

    private bool CanRun() => !_disposed && !IsBusy;

    private void NotifyCommands()
    {
        ReadCommand.NotifyCanExecuteChanged();
        EmphasizeCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
    }

    private async Task<string> ReadInventoryAsync(CancellationToken cancellationToken)
    {
        var version = _contextVersion;
        var result = await _actions.ReadAsync(cancellationToken);

        if (_disposed || version != _contextVersion)
            return string.Empty;

        if (result is not HostResult<LensPresentation>.Success success)
        {
            Groups = [];
            SpaceLabel = "Unavailable";
            return ((HostResult<LensPresentation>.Unavailable)result).Reason;
        }

        SpaceLabel = success.Value.SpaceLabel;
        LensLabel = success.Value.Label;
        Groups = success.Value.Groups;
        return HasGroups ? "Inventory updated for the current space." : success.Value.EmptyMessage;
    }

    private async Task RunAsync(Func<CancellationToken, Task<string>> action)
    {
        if (!CanRun())
            return;

        var version = _contextVersion;
        IsBusy = true;

        try
        {
            Status = "Working… Finish any active AutoCAD command to continue.";
            var message = await action(_lifetime.Token);

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
            IsBusy = false;
        }
    }
}