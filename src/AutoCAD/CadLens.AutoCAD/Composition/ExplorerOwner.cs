using System.ComponentModel;
using Trace = System.Diagnostics.Trace;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using CadLens.Core;
using CadLens.Lenses;
using CadLens.UI;
using Common.AutoCAD;
using Microsoft.Extensions.DependencyInjection;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using SystemVariableChangedEventArgs = Autodesk.AutoCAD.ApplicationServices.SystemVariableChangedEventArgs;

namespace CadLens.AutoCAD;

/// <summary>Owns one reusable plugin root and a separate scope for each open panel.</summary>
internal sealed class ExplorerOwner
{
    private static readonly EntityHighlightOptions HighlightColors = new(
        Accent: new EntityColor(70, 210, 230),
        Dimmed: new EntityColor(65, 72, 80));

    private readonly ServiceProvider _root = ExplorerComposition.Build(services =>
    {
        services.AddScoped<IHostTaskService, AutoCadTaskService>();
        services.AddScoped<ILayersSnapshotSource, AutoCadLayersSnapshotSource>();
        services.AddSingleton(HighlightColors);
        services.AddScoped<IEntityHighlightService, EntityHighlightService>();
        services.AddScoped<IEntityHighlightActions, EntityHighlightActions>();
        services.AddScoped<IHostActions, AutoCadHostActions>();
        services.AddScoped<IExplorerActions, ExplorerActions>();
    });

    private IServiceScope? _scope;
    private ExplorerWindow? _window;
    private ExplorerViewModel? _viewModel;
    private IHostTaskService? _requests;
    private IEntityHighlightService? _graphics;
    private Document? _observedDocument;
    private bool _refreshPending;
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
            _graphics = _scope.ServiceProvider.GetRequiredService<IEntityHighlightService>();
            _viewModel = _scope.ServiceProvider.GetRequiredService<ExplorerViewModel>();
            _viewModel.PropertyChanged += OnLensStateChanged;
            _window = _scope.ServiceProvider.GetRequiredService<ExplorerWindow>();
            _window.Closed += OnClosed;
            Application.DocumentManager.DocumentToBeDeactivated += OnContextLeaving;
            Application.DocumentManager.DocumentToBeDestroyed += OnContextLeaving;
            Application.DocumentManager.DocumentActivated += OnDocumentActivated;
            Application.SystemVariableChanged += OnSystemVariableChanged;
            Application.Idle += OnIdle;
            _viewModel.ResetContext(false);
            ObserveDocument(Application.DocumentManager.MdiActiveDocument);
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

    private void OnLensStateChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ExplorerViewModel.IsLensActive))
            _refreshPending = false;
    }

    private void OnClosed(object? sender, EventArgs args) => _ = CloseSessionAsync();

    private void OnContextLeaving(object sender, DocumentCollectionEventArgs args)
    {
        if (args.Document != _observedDocument)
            return;

        DetachDocument();
        _refreshPending = false;
        ResetContext(false);
    }

    private void OnDocumentActivated(object sender, DocumentCollectionEventArgs args) => ObserveDocument(args.Document);

    private void ObserveDocument(Document? document)
    {
        if (_observedDocument == document)
            return;

        DetachDocument();
        _observedDocument = document;

        if (document is not null)
        {
            document.Database.ObjectAppended += OnObjectChanged;
            document.Database.ObjectModified += OnObjectChanged;
            document.Database.ObjectErased += OnObjectErased;
            document.Database.ObjectUnappended += OnObjectChanged;
            document.Database.ObjectReappended += OnObjectChanged;
        }

        ResetContext(document is not null);
        _refreshPending = document is not null;
    }

    private void DetachDocument()
    {
        if (_observedDocument is null)
            return;

        _observedDocument.Database.ObjectAppended -= OnObjectChanged;
        _observedDocument.Database.ObjectModified -= OnObjectChanged;
        _observedDocument.Database.ObjectErased -= OnObjectErased;
        _observedDocument.Database.ObjectUnappended -= OnObjectChanged;
        _observedDocument.Database.ObjectReappended -= OnObjectChanged;
        _observedDocument = null;
    }

    private void OnObjectChanged(object sender, ObjectEventArgs args)
    {
        // Focus changes view records too; it must not trigger another inventory read.
        if (args.DBObject is Viewport && _observedDocument?.Editor.IsQuiescent == true)
            return;

        if (args.DBObject is Entity or LayerTableRecord)
            _refreshPending = true;
    }

    private void OnObjectErased(object sender, ObjectErasedEventArgs args) => _refreshPending = true;

    private void OnIdle(object? sender, EventArgs args)
    {
        if (_closing || _terminated)
            return;

        try
        {
            ObserveDocument(Application.DocumentManager.MdiActiveDocument);

            if (!_refreshPending || _observedDocument?.Editor.IsQuiescent != true ||
                _viewModel?.ReadCommand.CanExecute(null) != true)
                return;

            _refreshPending = false;
            // The view model contains async errors; drawing reads use the existing host queue.
            _ = _viewModel.ReadCommand.ExecuteAsync(null);
        }
        catch (Exception exception)
        {
            _refreshPending = false;
            Trace.TraceError("CAD Lens automatic refresh failed: {0}", exception);
        }
    }

    private void OnSystemVariableChanged(object sender, SystemVariableChangedEventArgs args)
    {
        if (args.Name is "CVPORT" or "CTAB" or "TILEMODE")
        {
            ResetContext(_observedDocument is not null);
            _refreshPending = _observedDocument is not null;
        }
    }

    private void ResetContext(bool hasDrawing)
    {
        _viewModel?.ResetContext(hasDrawing);
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
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged -= OnLensStateChanged;
                _viewModel.Dispose();
            }

            drained = _requests?.StopAsync() ?? Task.CompletedTask;
            Application.DocumentManager.DocumentToBeDeactivated -= OnContextLeaving;
            Application.DocumentManager.DocumentToBeDestroyed -= OnContextLeaving;
            Application.DocumentManager.DocumentActivated -= OnDocumentActivated;
            Application.SystemVariableChanged -= OnSystemVariableChanged;
            Application.Idle -= OnIdle;
            DetachDocument();
            _refreshPending = false;

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