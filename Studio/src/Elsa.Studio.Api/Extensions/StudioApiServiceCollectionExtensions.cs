using Elsa.Studio.Api.Contracts;
using Elsa.Studio.Api.Options;
using Elsa.Studio.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Api.Extensions;

public static class StudioApiServiceCollectionExtensions
{
    /// <summary>
    /// The configuration section that host/SDK versions and disabled-module ids are bound from.
    /// </summary>
    public const string ConfigurationSectionName = "Studio:Api";

    public static IServiceCollection AddElsaStudioApi(this IServiceCollection services, Action<StudioApiOptions>? configure = null)
    {
        var optionsBuilder = services.AddOptions<StudioApiOptions>();

        // Bind Studio:Api from configuration when available so HostVersion/SdkVersion can be supplied by
        // the host rather than falling back to the assembly version. Binding runs before the explicit
        // configure delegate so callers (e.g. the shell feature) retain the final say.
        optionsBuilder.Configure<IServiceProvider>((options, serviceProvider) =>
        {
            var configuration = serviceProvider.GetService<IConfiguration>();
            configuration?.GetSection(ConfigurationSectionName).Bind(options);
        });

        if (configure is not null)
            optionsBuilder.Configure(configure);

        services.AddScoped<IStudioModuleManifestProvider, StudioModuleManifestProvider>();
        return services;
    }
}
