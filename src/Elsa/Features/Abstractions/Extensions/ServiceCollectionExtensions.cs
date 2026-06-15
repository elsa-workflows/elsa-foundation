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
        return services.AddElsaCapability(new ElsaCapability(id, displayName, "1.0.0", sourceFeature, tags));
    }

    public static IServiceCollection AddElsaCapability(this IServiceCollection services, ElsaCapability capability)
    {
        services.AddElsaCapabilities();
        services.AddSingleton(capability);
        return services;
    }
}
