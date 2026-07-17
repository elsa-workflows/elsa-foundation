using Elsa.Persistence.Core.DependencyInjection;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Persistence.Groundwork.Stores;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Persistence.Groundwork.DependencyInjection;

public static class GroundworkStoreSessionRegistration
{
    /// <summary>Registers scoped Elsa adapters over one provider-owned admitted session source.</summary>
    public static IServiceCollection AddGroundworkStoreSessions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddPersistenceCore();
        services.TryAddSingleton<GroundworkStoreSessionSource>();
        services.TryAddSingleton<GroundworkWorkflowExecutionStatePageRouteSource>();
        services.TryAddSingleton<IGroundworkStoreSessionSource>(serviceProvider =>
            serviceProvider.GetRequiredService<GroundworkStoreSessionSource>());
        services.TryAddSingleton<GroundworkPrivilegedAccessSink>();
        services.TryAddScoped<IGroundworkPrivilegedAccessEmitter, GroundworkPrivilegedAccessRecorder>();
        services.TryAddScoped<IGroundworkStoreSessionFactory, GroundworkStoreSessionFactory>();
        services.TryAddScoped<GroundworkScopedDocumentStore>();

        services.RemoveAll<IDocumentStore>();
        services.RemoveAll<IBoundedDocumentStore>();
        services.AddScoped<IDocumentStore>(serviceProvider =>
            serviceProvider.GetRequiredService<GroundworkScopedDocumentStore>());
        services.AddScoped<IBoundedDocumentStore>(serviceProvider =>
            serviceProvider.GetRequiredService<GroundworkScopedDocumentStore>());
        return services;
    }
}
