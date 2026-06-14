using Elsa.Studio.Api.Contracts;
using Elsa.Studio.Api.Options;
using Elsa.Studio.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Api.Extensions;

public static class StudioApiServiceCollectionExtensions
{
    public static IServiceCollection AddElsaStudioApi(this IServiceCollection services, Action<StudioApiOptions>? configure = null)
    {
        if (configure is not null)
            services.Configure(configure);

        services.AddOptions<StudioApiOptions>();
        services.AddHttpClient();
        services.AddScoped<IBackendCapabilityProvider, BackendCapabilityProvider>();
        services.AddScoped<IStudioModuleManifestProvider, StudioModuleManifestProvider>();
        return services;
    }
}
