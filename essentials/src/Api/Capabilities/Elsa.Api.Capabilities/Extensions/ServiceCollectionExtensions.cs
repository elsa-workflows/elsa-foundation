using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Models;
using Elsa.Api.Capabilities.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Api.Capabilities.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApiCapabilities(this IServiceCollection services)
    {
        services.TryAddScoped<IApiCapabilityCatalog, ApiCapabilityCatalog>();
        return services;
    }

    public static IServiceCollection AddApiCapability(
        this IServiceCollection services,
        ApiCapabilityDeclaration declaration) => ApiCapabilityRegistration.Add(services, declaration);

    public static IServiceCollection AddApiCapabilitySource<TSource>(this IServiceCollection services)
        where TSource : class, IApiCapabilitySource
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IApiCapabilitySource, TSource>());
        return services;
    }
}
