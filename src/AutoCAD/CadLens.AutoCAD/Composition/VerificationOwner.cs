using System.Diagnostics;
using Autodesk.AutoCAD.ApplicationServices;
using CadLens.Lenses;
using CadLens.UI;
using Microsoft.Extensions.DependencyInjection;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadLens.AutoCAD;

/// <summary>Owns one reusable plugin root and a separate scope for each open panel.</summary>
internal sealed class VerificationOwner
{
    private readonly ServiceProvider _root = ExplorerComposition.Build(services =>
    {
        services.AddScoped<IHostTaskService, AutoCadTaskService>();
        services.AddScoped<ILayersSnapshotSource, AutoCadLayersSnapshotSource>();
        services.AddScoped<IVerificationGraphics, VerificationGraphics>();
        services.AddScoped<IVerificationActions, VerificationActions>();
    });

    private IServiceScope? _scope;
    private VerificationWindow? _window;
    private VerificationViewModel? _viewModel;
    private IHostTaskService? _requests;
    private IVerificationGraphics? _graphics;
    private bool _closing;
    private bool _terminated;
    private bool _rootDisposed;

    internal void Open()
    {
        if (_closing || _terminated)
            return;

        if (_window is not null)
        {
            _window.Activate();
            return;
        }

        _scope = _root.CreateScope();

        try
        {
            _requests = _scope.ServiceProvider.GetRequiredService<IHostTaskService>();
            _graphics = _scope.ServiceProvider.GetRequiredService<IVerificationGraphics>();
            _viewModel = _scope.ServiceProvider.GetRequiredService<VerificationViewModel>();
            _window = _scope.ServiceProvider.GetRequiredService<VerificationWindow>();
            _window.Closed += OnClosed;
            Application.DocumentManager.DocumentToBeDeactivated += OnContextLeaving;
            Application.DocumentManager.DocumentToBeDestroyed += OnContextLeaving;
            Application.SystemVariableChanged += OnSystemVariableChanged;
            Application.ShowModelessWindow(_window);
        }
        catch
        {
            _ = CloseSessionAsync();
            throw;
        }
    }

    internal void Terminate()
    {
        if (_terminated)
            return;

        // Shutdown cannot rely on a future Idle callback or access surviving document views.
        _terminated = true;
        _ = CloseSessionAsync();
    }

    private void OnClosed(object? sender, EventArgs args) => _ = CloseSessionAsync();

    private void OnContextLeaving(object sender, DocumentCollectionEventArgs args) => ResetContext();

    private void OnSystemVariableChanged(object sender, SystemVariableChangedEventArgs args)
    {
        if (args.Name is "CVPORT" or "CTAB" or "TILEMODE")
            ResetContext();
    }

    private void ResetContext()
    {
        _viewModel?.ResetContext();
        _graphics?.Clear();
    }

    private async Task CloseSessionAsync()
    {
        if (_closing)
            return;

        _closing = true;
        var drained = Task.CompletedTask;

        try
        {
            // Cancel pending work and detach native effects synchronously, before the first await.
            drained = _requests?.StopAsync() ?? Task.CompletedTask;
            Application.DocumentManager.DocumentToBeDeactivated -= OnContextLeaving;
            Application.DocumentManager.DocumentToBeDestroyed -= OnContextLeaving;
            Application.SystemVariableChanged -= OnSystemVariableChanged;

            if (_window is not null)
            {
                _window.Closed -= OnClosed;

                if (_window.IsVisible)
                    _window.Close();
            }

            _graphics?.Clear(redraw: !_terminated);
        }
        catch (Exception exception)
        {
            // Do not write to an Editor during shutdown: the document may already be destroyed.
            Trace.TraceError("CAD Lens cleanup failed: {0}", exception);
        }

        try
        {
            await drained;
            _scope?.Dispose();
        }
        catch (Exception exception)
        {
            Trace.TraceError("CAD Lens scope disposal failed: {0}", exception);
        }
        finally
        {
            _scope = null;
            _window = null;
            _viewModel = null;
            _requests = null;
            _graphics = null;
            _closing = false;

            if (_terminated && !_rootDisposed)
            {
                _rootDisposed = true;
                await _root.DisposeAsync();
            }
        }
    }
}