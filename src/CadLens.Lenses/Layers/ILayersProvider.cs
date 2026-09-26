using Common;

namespace CadLens.Lenses;

/// <summary>Supplies the Layers explorer inventory.</summary>
public interface ILayersProvider
{
    /// <summary>Builds the presentation for the current drawing and options.</summary>
    /// <param name="enabledFilters">Enabled filter identities.</param>
    /// <param name="cancellationToken">Cancellation of managed work.</param>
    Task<HostResult<LayersPresentation>> LoadAsync(
        IReadOnlySet<string> enabledFilters,
        CancellationToken cancellationToken);
}