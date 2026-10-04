using System.Windows.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace Common.AutoCAD;

/// <inheritdoc cref="IHostTaskService" />
public sealed class AutoCadTaskService : IHostTaskService, IDisposable
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly Queue<Action> _requests = new();
    private readonly TaskCompletionSource<bool> _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _stopping;
    private bool _running;

    /// <summary>Creates a queue on the current host dispatcher and subscribes to idle processing.</summary>
    public AutoCadTaskService() => Application.Idle += OnIdle;

    /// <inheritdoc />
    public async Task<HostResult<T>> RunAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<HostResult<T>>(TaskCreationOptions.RunContinuationsAsynchronously);

#if NETFRAMEWORK
        using var cancellation = cancellationToken.Register(() => CancelRequest(completion, cancellationToken));
#else
        await using var cancellation = cancellationToken.Register(() => CancelRequest(completion, cancellationToken));
#endif

        await _dispatcher.InvokeAsync(() =>
        {
            if (_stopping)
            {
                completion.TrySetResult(new HostResult<T>.Unavailable("The task service has stopped."));
                return;
            }

            var document = Application.DocumentManager.MdiActiveDocument;
            _requests.Enqueue(() => ExecuteRequest(document, action, completion, cancellationToken));
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

        if (_requests.Count != 0)
        {
            var request = _requests.Dequeue();
            _running = true;
            request();
        }
        else if (_stopping)
        {
            _stopped.TrySetResult(true);
        }
    }

    private void ExecuteRequest<T>(
        Document? document,
        Func<T> action,
        TaskCompletionSource<HostResult<T>> completion,
        CancellationToken cancellationToken)
    {
        if (_stopping || cancellationToken.IsCancellationRequested)
        {
            CompleteRequest(document, action, completion, cancellationToken);
            return;
        }

        try
        {
            Application.DocumentManager.ExecuteInApplicationContext(
                _ => CompleteRequest(document, action, completion, cancellationToken),
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
        TaskCompletionSource<HostResult<T>> completion,
        CancellationToken cancellationToken)
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
            CancelRequest(completion, cancellationToken);
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
            _stopped.TrySetResult(true);
        else
            _dispatcher.BeginInvoke(ProcessNextRequest);
    }

    private static void CancelRequest<T>(
        TaskCompletionSource<HostResult<T>> completion,
        CancellationToken cancellationToken)
    {
#if NETFRAMEWORK
        completion.TrySetException(new OperationCanceledException(cancellationToken));
#else
        completion.TrySetCanceled(cancellationToken);
#endif
    }
}
