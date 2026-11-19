using Elsa.Api.Capabilities.Exceptions;
using Elsa.Api.Capabilities.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Api.Capabilities;

/// <summary>Registers an immutable declaration without depending on the capability API implementation.</summary>
public static class ApiCapabilityRegistration
{
    public static IServiceCollection Add(IServiceCollection services, ApiCapabilityDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(declaration);
        var existing = services
            .Where(descriptor => descriptor.ServiceType == typeof(ApiCapabilityDeclaration))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ApiCapabilityDeclaration>()
            .Where(candidate => string.Equals(candidate.CapabilityId, declaration.CapabilityId, StringComparison.Ordinal))
            .ToArray();
        var conflicting = existing.FirstOrDefault(candidate => !candidate.IsEquivalentTo(declaration));
        if (conflicting is not null)
            throw new ApiCapabilityConflictException(
                $"Capability '{declaration.CapabilityId}' has incompatible declarations from features " +
                $"'{conflicting.SourceFeatureId}' and '{declaration.SourceFeatureId}'.");
        if (existing.Length > 0)
            return services;
        services.AddSingleton(declaration);
        return services;
    }
}
