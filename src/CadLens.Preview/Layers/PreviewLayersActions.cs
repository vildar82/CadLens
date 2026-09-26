using System.Collections.Immutable;
using CadLens.Lenses;
using CadLens.UI;
using Common;

namespace CadLens.Preview;

internal sealed class PreviewLayersActions(ILayersProvider provider, int delayMilliseconds) : ILayersActions
{
    /// <inheritdoc />
    public void ClearImmediately(bool hostTerminating) { }

    /// <inheritdoc />
    public async Task<HostResult<LayersPresentation>> ReadAsync(
        IReadOnlySet<string> enabledFilters,
        CancellationToken cancellationToken)
    {
        await Task.Delay(delayMilliseconds, cancellationToken);
        return await provider.LoadAsync(enabledFilters, cancellationToken);
    }

    /// <inheritdoc />
    public Task<string> IsolateObjectsAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken) =>
        DescribeAsync($"Preview: temporary isolation for {objects.Length:N0} sample objects.", cancellationToken);

    /// <inheritdoc />
    public async Task<HostResult<bool>> SelectAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken)
    {
        await Task.Delay(delayMilliseconds, cancellationToken);
        return new HostResult<bool>.Success(true);
    }

    /// <inheritdoc />
    public Task<HostResult<bool>> ClearIsolationAsync(CancellationToken cancellationToken) => ClearAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<HostResult<bool>> ClearAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(delayMilliseconds, cancellationToken);
        return new HostResult<bool>.Success(true);
    }

    /// <inheritdoc />
    public Task<string> FocusAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken) =>
        DescribeAsync($"Preview: Focus on {objects.Length:N0} sample objects; no drawing camera is connected.", cancellationToken);

    private async Task<string> DescribeAsync(string message, CancellationToken cancellationToken)
    {
        await Task.Delay(delayMilliseconds, cancellationToken);
        return message;
    }
}
