using CShells.Features;
using Elsa.Modularity.Core.Contracts;

namespace Elsa.Modularity.Nuplane.Services;

public sealed class RuntimeFeatureCatalogRefresher(IRuntimeFeatureCatalog runtimeFeatureCatalog) : IRuntimeFeatureCatalogRefresher
{
    public async Task<int> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await runtimeFeatureCatalog.RefreshAsync(cancellationToken);
        return snapshot.FeatureDescriptors.Count;
    }
}
