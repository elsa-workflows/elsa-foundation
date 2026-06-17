using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Nuplane.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Modularity.Nuplane.Extensions;

public static class ModularityNuplaneServiceCollectionExtensions
{
    public static IServiceCollection AddNuplaneFeatureCatalog(this IServiceCollection services)
    {
        services.TryAddScoped<IFeatureManagementService, FeatureManagementService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFeatureCatalogContributor, RuntimeFeatureCatalogContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFeatureCatalogContributor, PackageManifestFeatureCatalogContributor>());
        services.TryAddScoped<IRuntimeFeatureCatalogAccessor, RuntimeFeatureCatalogAccessor>();
        services.TryAddScoped<IRuntimeFeatureCatalogRefresher, RuntimeFeatureCatalogRefresher>();
        return services;
    }
}
