using Elsa.Features.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Features.Abstractions.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddElsaCapabilities(this IServiceCollection services)
    {
        services.TryAddSingleton<IElsaCapabilityProvider, ElsaCapabilityProvider>();
        return services;
    }

    public static IServiceCollection AddElsaCapability(
        this IServiceCollection services,
        string id,
        string displayName,
        string sourceFeature,
        params string[] tags)
    {
        var version = System.Reflection.Assembly.GetCallingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        return services.AddElsaCapability(new ElsaCapability(id, displayName, version, sourceFeature, tags));
    }

    public static IServiceCollection AddElsaCapability(this IServiceCollection services, ElsaCapability capability)
    {
        services.AddElsaCapabilities();
        services.AddSingleton(capability);
        return services;
    }
}
