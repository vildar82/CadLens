namespace Common.AutoCAD;

/// <summary>Runs queued work in AutoCAD's application context with the document locked.</summary>
public interface IHostTaskService
{
    /// <summary>Queues work against the drawing active when the request is submitted.</summary>
    /// <typeparam name="T">Operation result type.</typeparam>
    /// <param name="action">Synchronous work performed with the document locked.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    Task<HostResult<T>> RunAsync<T>(Func<T> action, CancellationToken cancellationToken);

    /// <summary>Stops accepting work and waits for the running callback before disposal.</summary>
    Task StopAsync();
}