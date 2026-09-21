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
        // Bundled + host-referenced features are discovered by CShells' runtime feature catalog (IRuntimeFeatureCatalog,
        // registered by AddCShells); this contributor projects that catalog into the feature listing. The package
        // contributor adds features from installed Nuplane packages on top.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFeatureCatalogContributor, RuntimeFeatureCatalogContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFeatureCatalogContributor, PackageManifestFeatureCatalogContributor>());
        services.TryAddScoped<IRuntimeFeatureCatalogRefresher, RuntimeFeatureCatalogRefresher>();
        return services;
    }
}
