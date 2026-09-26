using System.Collections.Immutable;
using System.Windows;
using Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CadLens.UI;

/// <summary>Discovers lens modules and coordinates their lifetime without knowing their content.</summary>
public sealed class ExplorerViewModel : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _activationRequest;
    private Task _activation = Task.CompletedTask;
    private LensOption? _selectedLens;
    private string _status = "Activate a lens to explore the drawing.";
    private bool _isCleanupPending;
    private bool _hasDrawing = true;
    private bool _disposed;
    private int _contextVersion;

    /// <summary>Creates toolbar controls from the scoped module registrations.</summary>
    /// <param name="lenses">Independent lens implementations in display order.</param>
    public ExplorerViewModel(IEnumerable<ILens> lenses)
    {
        Lenses = [.. lenses.Select(lens => new LensOption(lens))];

        if (Lenses.IsEmpty)
            throw new InvalidOperationException("Register at least one lens in DI.");

        if (Lenses.Select(lens => lens.Descriptor.Id).Distinct(StringComparer.Ordinal).Count() != Lenses.Length)
            throw new InvalidOperationException("Registered lens identities must be unique.");

        ToggleLensCommand = new AsyncRelayCommand<LensOption>(ToggleLensAsync, CanToggleLens, AsyncRelayCommandOptions.AllowConcurrentExecutions);
    }

    /// <summary>Registered lens modules, without creating their views or reading a drawing.</summary>
    public ImmutableArray<LensOption> Lenses { get; }

    /// <summary>Activates a module, switches modules, or collapses the active module.</summary>
    public IAsyncRelayCommand<LensOption> ToggleLensCommand { get; }

    /// <summary>Whether a module currently occupies the expanded panel.</summary>
    public bool IsLensActive => _selectedLens?.IsActive == true;

    /// <summary>The selected module's own WPF content; the shell imposes no view-model contract.</summary>
    public FrameworkElement? ActiveView => IsLensActive ? _selectedLens!.Lens.View : null;

    /// <summary>Whether previous work and module cleanup are still settling.</summary>
    public bool IsCleanupPending
    {
        get => _isCleanupPending;
        private set
        {
            if (SetProperty(ref _isCleanupPending, value))
                ToggleLensCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Activation or cleanup status shown by the shared chrome.</summary>
    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>Forwards context invalidation to all modules, including inactive ones.</summary>
    /// <param name="hasDrawing">Whether drawing-dependent activation is available.</param>
    public void ResetContext(bool hasDrawing = true)
    {
        if (_disposed)
            return;

        _contextVersion++;
        _hasDrawing = hasDrawing;

        foreach (var option in Lenses)
        {
            try
            {
                option.Lens.OnContextChanged(hasDrawing);
            }
            catch (Exception exception)
            {
                Status = $"{option.Descriptor.Label}: {exception.Message}";
            }
        }

        ToggleLensCommand.NotifyCanExecuteChanged();
    }

    /// <inheritdoc />
    public void Dispose() => Close(false);

    /// <summary>Closes every module before the host queue and DI scope are disposed.</summary>
    /// <param name="hostTerminating">Whether the host is shutting down.</param>
    public void Close(bool hostTerminating)
    {
        if (_disposed)
            return;

        _disposed = true;
        _lifetime.Cancel();

        foreach (var option in Lenses)
        {
            option.IsActive = false;

            try
            {
                option.Lens.Close(hostTerminating);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Trace.TraceError("Lens disposal failed: {0}", exception);
            }
        }

        _activationRequest?.Dispose();
        _lifetime.Dispose();
        NotifyLensState();
    }

    private bool CanToggleLens(LensOption? lens) =>
        !_disposed && lens is not null && Lenses.Contains(lens) && !IsCleanupPending &&
        (_hasDrawing || (IsLensActive && ReferenceEquals(lens, _selectedLens)));

    private async Task ToggleLensAsync(LensOption? lens)
    {
        if (!CanToggleLens(lens))
            return;

        var collapseOnly = IsLensActive && ReferenceEquals(lens, _selectedLens);

        if (_selectedLens is not null && !await DeactivateAsync())
            return;

        if (collapseOnly || _disposed || !_hasDrawing)
            return;

        _activation = ActivateAsync(lens!);
        await _activation;
    }

    private async Task ActivateAsync(LensOption option)
    {
        var version = _contextVersion;
        _activationRequest = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _selectedLens = option;
        option.IsActive = true;
        Status = $"{option.Descriptor.Label} is active.";
        NotifyLensState();

        try
        {
            await option.Lens.ActivateAsync(_activationRequest.Token);
        }
        catch (OperationCanceledException)
        {
            // Deactivation or close owns the next visible state.
        }
        catch (Exception exception)
        {
            if (!_disposed && version == _contextVersion)
                Status = $"Unable to activate {option.Descriptor.Label}: {exception.Message}";
        }
    }

    private async Task<bool> DeactivateAsync()
    {
        var version = ++_contextVersion;
        var option = _selectedLens!;
        var cancellationToken = _lifetime.Token;
        IsCleanupPending = true;
        option.IsActive = false;
        _activationRequest?.Cancel();
        Status = "Clearing lens effects… Waiting for AutoCAD.";
        NotifyLensState();

        try
        {
            await _activation;
            cancellationToken.ThrowIfCancellationRequested();
            var result = await option.Lens.DeactivateAsync(cancellationToken);

            if (result is HostResult<bool>.Success { Value: true })
                _selectedLens = null;

            if (!_disposed && version == _contextVersion)
                Status = result.Match(
                    cleared => cleared ? "Lens effects cleared." : "Cleanup did not complete.",
                    reason => $"Cleanup unavailable: {reason}");

            return !_disposed && version == _contextVersion && _selectedLens is null;
        }
        catch (Exception exception)
        {
            if (!_disposed && version == _contextVersion)
                Status = $"Cleanup did not complete: {exception.Message}";

            return false;
        }
        finally
        {
            _activationRequest?.Dispose();
            _activationRequest = null;
            IsCleanupPending = false;
        }
    }

    private void NotifyLensState()
    {
        OnPropertyChanged(nameof(IsLensActive));
        OnPropertyChanged(nameof(ActiveView));
        ToggleLensCommand.NotifyCanExecuteChanged();
    }
}
