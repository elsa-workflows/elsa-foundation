using Elsa.Foundation.Identity.Extensions;
using Elsa.Modularity.Api.Authorization;
using Elsa.Modularity.Api.Options;
using Elsa.Modularity.Api.Services;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Nuplane.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Modularity.Api.Extensions;

public static class ModularityApiServiceCollectionExtensions
{
    public static IServiceCollection AddModularityApi(this IServiceCollection services, Action<FeatureManagementOptions>? configure = null)
    {
        if (configure is not null)
            services.Configure(configure);

        services.AddPermissionContributor<ModuleManagementPermissionContributor>();
        services.AddNuplaneFeatureCatalog();
        // After the runtime contributor, which supplies each feature's class: a dormant feature's availability and reason
        // (spec 182, FR-009), from the shared dormancy check the host composes. A host that composes none has every
        // declared requirement reported unmet, because it cannot tell, rather than available.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFeatureCatalogContributor, FeatureAvailabilityCatalogContributor>());
        services.RemoveAll<IShellFeatureConfigurationStore>();
        services.RemoveAll<IShellReloader>();
        services.AddScoped<IShellFeatureConfigurationStore, JsonShellFeatureConfigurationStore>();
        services.AddScoped<IShellReloader, ShellReloader>();

        return services;
    }
}
