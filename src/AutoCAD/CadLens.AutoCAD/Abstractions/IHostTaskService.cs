using CadLens.Core;

namespace CadLens.AutoCAD;

/// <summary>Runs queued work in AutoCAD's application context with the document locked.</summary>
internal interface IHostTaskService
{
    Task<HostResult<T>> RunAsync<T>(Func<T> action, CancellationToken cancellationToken);

    /// <summary>Stops accepting work and waits for the running callback before disposal.</summary>
    Task StopAsync();
}