using System.Collections.Immutable;
using CadLens.Lenses;
using CadLens.UI;
using Common;

namespace CadLens.Preview;

internal sealed class PreviewLayersActions(ILayersProvider provider, int delayMilliseconds) : ILayersActions
{
    /// <inheritdoc />
    public void ClearImmediately(bool redraw) { }

    /// <inheritdoc />
    public async Task<HostResult<LayersPresentation>> ReadAsync(
        IReadOnlySet<string> enabledFilters,
        CancellationToken cancellationToken)
    {
        await Task.Delay(delayMilliseconds, cancellationToken);
        return await provider.LoadAsync(enabledFilters, cancellationToken);
    }

    /// <inheritdoc />
    public Task<string> EmphasizeAsync(CancellationToken cancellationToken) =>
        DescribeAsync("Preview: CAD preselection highlighting is simulated.", cancellationToken);

    /// <inheritdoc />
    public Task<string> EmphasizeObjectsAsync(ImmutableArray<IPlacedObjectId> objects, CancellationToken cancellationToken) =>
        DescribeAsync($"Preview: temporary emphasis for {objects.Length:N0} sample objects.", cancellationToken);

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
