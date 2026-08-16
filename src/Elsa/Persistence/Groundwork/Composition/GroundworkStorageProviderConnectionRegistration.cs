using Groundwork.Store;
using Elsa.Persistence.Groundwork.Targets;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.Composition;

/// <summary>
/// Registers the host-selected public Groundwork v2 provider connection for Elsa adapters.
/// Provider packages own construction and lifetime; this composition seam only exposes the
/// already-created connection to feature registrations.
/// </summary>
public static class GroundworkStorageProviderConnectionRegistration
{
    public static IServiceCollection AddGroundworkStorageProviderConnection(
        this IServiceCollection services,
        IStorageProviderConnection connection,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connection);
        var target = GroundworkTargetNames.Normalize(targetName);

        var existing = services.SingleOrDefault(descriptor =>
            descriptor.ServiceType == typeof(IStorageProviderConnection) &&
            descriptor.IsKeyedService &&
            Equals(descriptor.ServiceKey, target));
        if (existing is not null)
        {
            if (ReferenceEquals(existing.KeyedImplementationInstance, connection))
                return services;
            throw new InvalidOperationException(
                $"Groundwork target '{target}' already has a v2 provider connection. " +
                "Give each physical store a distinct target name.");
        }

        services.AddKeyedSingleton<IStorageProviderConnection>(target, connection);
        if (GroundworkTargetNames.IsDefault(target))
            services.AddSingleton(connection);
        return services;
    }
}
