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
    /// <summary>
    /// Registers a lazily-created provider connection and gives the service provider ownership of its lifetime.
    /// The default target is available through both ordinary and keyed resolution; named targets are keyed only.
    /// </summary>
    /// <param name="services">The host service collection.</param>
    /// <param name="connectionFactory">Creates the one connection owned by this target.</param>
    /// <param name="targetName">The optional physical-store target name.</param>
    public static IServiceCollection AddGroundworkStorageProviderConnection(
        this IServiceCollection services,
        Func<IServiceProvider, IStorageProviderConnection> connectionFactory,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connectionFactory);
        var target = GroundworkTargetNames.Normalize(targetName);
        if (FindTargetRegistration(services, target) is not null)
        {
            throw new InvalidOperationException(
                $"Groundwork target '{target}' already has a v2 provider connection. " +
                "Give each physical store a distinct target name.");
        }

        if (GroundworkTargetNames.IsDefault(target))
        {
            services.AddSingleton(connectionFactory);
            services.AddKeyedSingleton<IStorageProviderConnection>(target, (provider, _) =>
                provider.GetRequiredService<IStorageProviderConnection>());
        }
        else
        {
            services.AddKeyedSingleton<IStorageProviderConnection>(target, (provider, _) =>
                connectionFactory(provider));
        }
        return services;
    }

    public static IServiceCollection AddGroundworkStorageProviderConnection(
        this IServiceCollection services,
        IStorageProviderConnection connection,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connection);
        var target = GroundworkTargetNames.Normalize(targetName);

        var existing = FindTargetRegistration(services, target);
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

    private static ServiceDescriptor? FindTargetRegistration(IServiceCollection services, string target) =>
        services.SingleOrDefault(descriptor =>
            descriptor.ServiceType == typeof(IStorageProviderConnection) &&
            descriptor.IsKeyedService &&
            Equals(descriptor.ServiceKey, target));
}
