using System.Windows.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadLens.Common.AutoCAD;

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

        var state = 0;
#if NETFRAMEWORK
        using var cancellation = cancellationToken.Register
#else
        await using var cancellation = cancellationToken.Register
#endif
        (() =>
        {
            // A running callback owns its completion until its native resources and document lock are released.
            if (Interlocked.CompareExchange(ref state, 2, 0) == 0)
                CancelRequest(completion, cancellationToken);
        });

        var enqueue = _dispatcher.InvokeAsync(() =>
        {
            if (completion.Task.IsCompleted)
                return;

            if (_stopping)
            {
                completion.TrySetResult(new HostResult<T>.Unavailable("The task service has stopped."));
                return;
            }

            var document = Application.DocumentManager.MdiActiveDocument;
            _requests.Enqueue(() => ExecuteRequest(
                document,
                action,
                completion,
                () => Interlocked.CompareExchange(ref state, 1, 0) == 0,
                cancellationToken));
            ProcessNextRequest();
        });

        _ = enqueue.Task.ContinueWith(task =>
        {
            if (task.IsCanceled)
                completion.TrySetResult(new HostResult<T>.Unavailable("The task service has stopped."));
            else if (task.IsFaulted)
                completion.TrySetResult(new HostResult<T>.Unavailable(task.Exception!.GetBaseException().Message));
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        return await completion.Task.ConfigureAwait(false);
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
        Func<bool> tryStart,
        CancellationToken cancellationToken)
    {
        if (_stopping || cancellationToken.IsCancellationRequested)
        {
            CompleteRequest(document, action, completion, tryStart, cancellationToken);
            return;
        }

        try
        {
            Application.DocumentManager.ExecuteInApplicationContext(
                _ => CompleteRequest(document, action, completion, tryStart, cancellationToken),
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
        Func<bool> tryStart,
        CancellationToken cancellationToken)
    {
        // Keep managed exceptions inside the native callback.
        try
        {
            if (!tryStart())
                return;

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

            cancellationToken.ThrowIfCancellationRequested();
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
