using Elsa.Modularity.Core.Models;

namespace Elsa.Modularity.Core.Contracts;

public interface IFeatureCatalogContributor
{
    Task ContributeAsync(FeatureCatalogContributionContext context, CancellationToken cancellationToken = default);
}
