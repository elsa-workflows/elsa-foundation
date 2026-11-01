using Elsa.Studio.Core.Contracts;
using Elsa.Studio.Core.Models;

namespace Elsa.Studio.Backends;

/// <summary>Explicit optional host integration; normal feature activation does not modify the root container.</summary>
public static class BackendsHostConfigurationExtensions
{
    public static IServiceCollection AddStudioBackendsHostConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IStudioBackendAuthoritySource)))
            throw new InvalidOperationException("A Studio backend authority source is already registered.");
        services.AddSingleton<IStudioBackendAuthoritySource>(new BackendsAuthoritySource(configuration));
        return services;
    }

    private sealed class BackendsAuthoritySource(IConfiguration configuration) : IStudioBackendAuthoritySource
    {
        public StudioBackendAuthority Resolve(StudioBackendAuthorityContext context)
        {
            if (!context.EnabledFeatures.Contains("BackendsStudio", StringComparer.OrdinalIgnoreCase)
                || context.DisabledFeatures.Contains("BackendsStudio", StringComparer.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(context.Configured.BrowserBaseUrl))
                return context.Configured;
            // Only effective feature enablement permits reading optional configuration.
            _ = StudioBackendProfiles.ReadConfiguration(configuration);
            var browser = StudioBackendProfiles.BrowserAuthority(configuration);
            return new(browser, string.IsNullOrWhiteSpace(context.Configured.ServerBaseUrl) ? browser : context.Configured.ServerBaseUrl);
        }
    }
}
