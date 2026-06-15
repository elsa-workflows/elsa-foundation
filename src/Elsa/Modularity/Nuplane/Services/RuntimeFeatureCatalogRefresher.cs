using Elsa.Modularity.Core.Contracts;

namespace Elsa.Modularity.Nuplane.Services;

public sealed class RuntimeFeatureCatalogRefresher(IServiceProvider serviceProvider) : IRuntimeFeatureCatalogRefresher
{
    public async Task<int> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await RuntimeFeatureCatalogReflection.RefreshAsync(serviceProvider, cancellationToken);
        return RuntimeFeatureCatalogReflection.GetFeatureDescriptorCount(snapshot);
    }
}
