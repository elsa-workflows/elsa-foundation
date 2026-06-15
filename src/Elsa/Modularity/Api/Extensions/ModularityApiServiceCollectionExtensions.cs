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

        services.AddNuplaneFeatureCatalog();
        services.TryAddScoped<IShellFeatureConfigurationStore, JsonShellFeatureConfigurationStore>();
        services.TryAddScoped<IShellReloader, ShellReloader>();

        return services;
    }
}
