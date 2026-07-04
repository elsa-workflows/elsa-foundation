using CShells.Features;

namespace Elsa.Studio.Web;

internal sealed class StudioRuntimeFeatureCatalogRefresher(
    IRuntimeFeatureCatalog catalog,
    ILogger<StudioRuntimeFeatureCatalogRefresher> logger)
{
    public async Task<StudioRuntimeFeatureCatalogRefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await catalog.RefreshAsync(cancellationToken).ConfigureAwait(false);
        var generation = snapshot.Generation;
        var featureCount = snapshot.FeatureDescriptors.Count;

        logger.LogDebug(
            "Refreshed CShells runtime feature catalog generation {Generation} with {FeatureCount} feature(s).",
            generation,
            featureCount);

        return new StudioRuntimeFeatureCatalogRefreshResult(generation, featureCount);
    }

    public async Task<IReadOnlyCollection<ShellFeatureDescriptor>> GetFeatureDescriptorsAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await catalog.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return snapshot.FeatureDescriptors;
    }
}

internal sealed record StudioRuntimeFeatureCatalogRefreshResult(long? Generation, int? FeatureCount);
