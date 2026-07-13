using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Exceptions;
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
        ApiCapabilityDeclaration declaration)
    {
        var existing = services
            .Where(descriptor => descriptor.ServiceType == typeof(ApiCapabilityDeclaration))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ApiCapabilityDeclaration>()
            .Where(candidate => string.Equals(candidate.CapabilityId, declaration.CapabilityId, StringComparison.Ordinal))
            .ToArray();
        var conflicting = existing.FirstOrDefault(candidate => !candidate.IsCompatibleWith(declaration));
        if (conflicting is not null)
            throw new ApiCapabilityConflictException(
                $"Capability '{declaration.CapabilityId}' has incompatible declarations from features " +
                $"'{conflicting.SourceFeatureId}' and '{declaration.SourceFeatureId}'.");
        if (existing.Length > 0)
            return services;

        services.AddSingleton(declaration);
        return services;
    }

    public static IServiceCollection AddApiCapabilitySource<TSource>(this IServiceCollection services)
        where TSource : class, IApiCapabilitySource
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IApiCapabilitySource, TSource>());
        return services;
    }
}
