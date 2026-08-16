using Groundwork.Store;
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
        IStorageProviderConnection connection)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connection);
        services.AddSingleton<IStorageProviderConnection>(connection);
        return services;
    }
}
