using Elsa.Modularity.Core.Models;

namespace Elsa.Modularity.Core.Contracts;

public interface IFeatureManagementService
{
    Task<FeatureCatalogResponse> GetCatalogAsync(CancellationToken cancellationToken = default);

    Task<FeatureApplyResult> ApplyAsync(FeatureApplyRequest request, CancellationToken cancellationToken = default);
}
