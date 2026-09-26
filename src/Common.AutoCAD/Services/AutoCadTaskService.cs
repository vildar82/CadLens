using System.Windows.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace Common.AutoCAD;

/// <inheritdoc cref="IHostTaskService" />
public sealed class AutoCadTaskService : IHostTaskService, IDisposable
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly Queue<Action> _requests = new();
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _stopping;
    private bool _running;

    /// <summary>Creates a queue on the current host dispatcher and subscribes to idle processing.</summary>
    public AutoCadTaskService() => Application.Idle += OnIdle;

    /// <inheritdoc />
    public async Task<HostResult<T>> RunAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<HostResult<T>>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var cancellation = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));

        await _dispatcher.InvokeAsync(() =>
        {
            if (_stopping)
            {
                completion.TrySetResult(new HostResult<T>.Unavailable("The task service has stopped."));
                return;
            }

            var document = Application.DocumentManager.MdiActiveDocument;
            _requests.Enqueue(() => ExecuteRequest(document, action, cancellationToken, completion));
            ProcessNextRequest();
        });

        return await completion.Task;
    }

    /// <inheritdoc />
    public Task StopAsync()
    {
        _dispatcher.VerifyAccess();
        _stopping = true;
        ProcessNextRequest();

        return _stopped.Task;
    }

    /// <summary>Detaches idle processing after StopAsync completes.</summary>
    public void Dispose() => Application.Idle -= OnIdle;

    private void OnIdle(object? sender, EventArgs args) => ProcessNextRequest();

    private void ProcessNextRequest()
    {
        if (_running)
            return;

        if (!_stopping && Convert.ToInt32(Application.GetSystemVariable("CMDACTIVE")) != 0)
            return;

        if (_requests.TryDequeue(out var request))
        {
            _running = true;
            request();
        }
        else if (_stopping)
        {
            _stopped.TrySetResult();
        }
    }

    private void ExecuteRequest<T>(
        Document? document,
        Func<T> action,
        CancellationToken cancellationToken,
        TaskCompletionSource<HostResult<T>> completion)
    {
        if (_stopping || cancellationToken.IsCancellationRequested)
        {
            CompleteRequest(document, action, cancellationToken, completion);
            return;
        }

        try
        {
            Application.DocumentManager.ExecuteInApplicationContext(
                _ => CompleteRequest(document, action, cancellationToken, completion),
                null!);
        }
        catch (Exception exception)
        {
            completion.TrySetResult(new HostResult<T>.Unavailable(exception.Message));
            RequestFinished();
        }
    }

    private void CompleteRequest<T>(
        Document? document,
        Func<T> action,
        CancellationToken cancellationToken,
        TaskCompletionSource<HostResult<T>> completion)
    {
        // Keep managed exceptions inside the native callback.
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            HostResult<T> result;

            if (_stopping)
                result = new HostResult<T>.Unavailable("The task service has stopped.");
            else if (document is null || document != Application.DocumentManager.MdiActiveDocument)
                result = new HostResult<T>.Unavailable("The active drawing is no longer available.");
            else
            {
                using (document.LockDocument())
                    result = new HostResult<T>.Success(action());
            }

            completion.TrySetResult(result);
        }
        catch (OperationCanceledException)
        {
            completion.TrySetCanceled(cancellationToken);
        }
        catch (Exception exception)
        {
            completion.TrySetResult(new HostResult<T>.Unavailable(exception.Message));
        }
        finally
        {
            RequestFinished();
        }
    }

    private void RequestFinished()
    {
        _running = false;

        if (_stopping && _requests.Count == 0)
            _stopped.TrySetResult();
        else
            _dispatcher.BeginInvoke(ProcessNextRequest);
    }
}
