using Common;

namespace CadLens.Core;

/// <summary>Supplies immutable presentation data for a lens.</summary>
public interface ILensProvider
{
    /// <summary>Builds the presentation for the current drawing and options.</summary>
    /// <param name="enabledFilters">Enabled filter identities.</param>
    /// <param name="cancellationToken">Cancellation of managed work.</param>
    Task<HostResult<LensPresentation>> LoadAsync(
        IReadOnlySet<string> enabledFilters,
        CancellationToken cancellationToken);
}