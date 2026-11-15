namespace Elsa.Modularity.Core.Contracts;

public interface IRuntimeFeatureCatalogRefresher
{
    Task<int> RefreshAsync(CancellationToken cancellationToken = default);
}
