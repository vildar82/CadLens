using System.Windows.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using Common;
using Common.AutoCAD;
using Xunit;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadLens.AutoCAD;

/// <summary>Checks the production queue with a real WPF dispatcher and stubbed native AutoCAD callbacks.</summary>
public sealed class AutoCadTaskServiceTests
{
    /// <summary>Requests run in submission order, one at a time, with application context and a document lock.</summary>
    [Fact]
    public Task ExecutesInOrderWithDocumentLocked() => OnUiThread(async () =>
    {
        using var service = new AutoCadTaskService();
        var documents = Application.DocumentManager;
        var calls = new List<int>();
        var requests = Enumerable.Range(1, 3).Select(index => service.RunAsync(
            () =>
            {
                Assert.True(documents.IsApplicationContext);
                Assert.True(documents.MdiActiveDocument!.IsLocked);
                calls.Add(index);
                return index;
            },
            default)).ToArray();

        for (var index = 0; index < requests.Length; index++)
        {
            await Dispatcher.Yield();
            Assert.Equal(1, documents.PendingCount);
            documents.ExecuteNext();
            Assert.Equal(index + 1, Assert.IsType<HostResult<int>.Success>(await requests[index]).Value);
            Assert.False(documents.MdiActiveDocument!.IsLocked);
        }

        Assert.Equal(new[] { 1, 2, 3 }, calls);
        await service.StopAsync();
    });

    /// <summary>Busy commands delay requests until Idle; an already idle drawing starts without waiting for an event.</summary>
    [Fact]
    public Task WaitsForBusyDrawing() => OnUiThread(async () =>
    {
        using var service = new AutoCadTaskService();
        var documents = Application.DocumentManager;
        documents.ActiveCommand = 1;
        var request = service.RunAsync(() => true, default);

        await Dispatcher.Yield();
        Assert.Equal(0, documents.PendingCount);
        documents.ActiveCommand = 0;
        Application.RaiseIdle();
        Assert.Equal(1, documents.PendingCount);
        documents.ExecuteNext();

        Assert.IsType<HostResult<bool>.Success>(await request);
        await service.StopAsync();
    });

    /// <summary>A modeless panel can run work when no command is active even if the editor reports non-quiescent.</summary>
    [Fact]
    public Task RunsWithoutActiveCommandWhenEditorIsNotQuiescent() => OnUiThread(async () =>
    {
        using var service = new AutoCadTaskService();
        var documents = Application.DocumentManager;
        documents.MdiActiveDocument!.Editor.IsQuiescent = false;
        var request = service.RunAsync(() => true, default);

        await Dispatcher.Yield();
        Assert.Equal(1, documents.PendingCount);
        documents.ExecuteNext();

        Assert.IsType<HostResult<bool>.Success>(await request);
        await service.StopAsync();
    });

    /// <summary>A native callback queued for another drawing must not execute the action.</summary>
    [Fact]
    public Task RejectsChangedDocument() => OnUiThread(async () =>
    {
        using var service = new AutoCadTaskService();
        var called = false;
        var request = service.RunAsync(() => called = true, default);

        await Dispatcher.Yield();
        Application.DocumentManager.MdiActiveDocument = new Document();
        Application.DocumentManager.ExecuteNext();

        Assert.IsType<HostResult<bool>.Unavailable>(await request);
        Assert.False(called);
        await service.StopAsync();
    });

    /// <summary>Cancellation wakes the caller even while AutoCAD is busy, and the cancelled action is skipped.</summary>
    [Fact]
    public Task CancelsWhileWaitingForIdle() => OnUiThread(async () =>
    {
        using var service = new AutoCadTaskService();
        using var cancellation = new CancellationTokenSource();
        Application.DocumentManager.ActiveCommand = 1;
        var called = false;
        var request = service.RunAsync(() => called = true, cancellation.Token);

        await Dispatcher.Yield();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await service.StopAsync();

        Assert.False(called);
        Assert.Equal(0, Application.DocumentManager.PendingCount);
    });

    /// <summary>Close drains the native callback and waiting work without performing their actions.</summary>
    [Fact]
    public Task StopDrainsPendingWorkAndRejectsNewWork() => OnUiThread(async () =>
    {
        using var service = new AutoCadTaskService();
        var called = false;
        var first = service.RunAsync(() => called = true, default);
        var second = service.RunAsync(() => called = true, default);

        await Dispatcher.Yield();
        var stopped = service.StopAsync();
        Assert.False(stopped.IsCompleted);
        Application.DocumentManager.ExecuteNext();
        await stopped;

        Assert.IsType<HostResult<bool>.Unavailable>(await first);
        Assert.IsType<HostResult<bool>.Unavailable>(await second);
        Assert.IsType<HostResult<bool>.Unavailable>(await service.RunAsync(() => true, default));
        Assert.False(called);
    });

    /// <summary>A failed callback does not escape into native code or prevent the next request from running.</summary>
    [Fact]
    public Task ContainsCallbackFailureAndContinues() => OnUiThread(async () =>
    {
        using var service = new AutoCadTaskService();
        var failed = service.RunAsync<bool>(() => throw new InvalidOperationException("fixture"), default);
        var next = service.RunAsync(() => true, default);

        await Dispatcher.Yield();
        Application.DocumentManager.ExecuteNext();
        Assert.Contains("fixture", Assert.IsType<HostResult<bool>.Unavailable>(await failed).Reason);
        await Dispatcher.Yield();
        Application.DocumentManager.ExecuteNext();

        Assert.IsType<HostResult<bool>.Success>(await next);
        await service.StopAsync();
    });

    /// <summary>Failure to enter application context completes the request and releases the queue.</summary>
    [Fact]
    public Task HandlesNativeSchedulingFailure() => OnUiThread(async () =>
    {
        using var service = new AutoCadTaskService();
        Application.DocumentManager.SchedulingError = new InvalidOperationException("native failure");

        var result = await service.RunAsync(() => true, default);

        Assert.Equal("native failure", Assert.IsType<HostResult<bool>.Unavailable>(result).Reason);
        await service.StopAsync();
    });

    private static Task OnUiThread(Func<Task> test)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            Application.DocumentManager = new DocumentCollection();
            dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await test();
                    completion.TrySetResult();
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
                finally
                {
                    dispatcher.InvokeShutdown();
                }
            });
            Dispatcher.Run();
        }) { IsBackground = true };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }
}
