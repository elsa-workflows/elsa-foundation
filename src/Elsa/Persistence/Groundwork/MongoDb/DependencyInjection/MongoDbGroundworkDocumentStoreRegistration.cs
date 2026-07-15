using CShells.Lifecycle;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Unified.DependencyInjection;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Elsa.Persistence.Groundwork.MongoDb.DependencyInjection;

/// <summary>
/// Registers the MongoDB provider leaf. The document store remains unavailable until the startup
/// initializer has admitted a transaction-capable replica set and the exact deployment-owned schema.
/// </summary>
public static class MongoDbGroundworkDocumentStoreRegistration
{
    public static IServiceCollection AddMongoDbGroundworkDocumentStore(
        this IServiceCollection services,
        string connectionString,
        string databaseName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        services.SelectGroundworkProviderLeaf("mongodb");

        if (services.Any(descriptor =>
                descriptor.ServiceType == typeof(MongoDbGroundworkDocumentStoreInitializer)))
        {
            return services;
        }

        services.AddGroundworkStorageComposition();
        services.RemoveAll<IDocumentStore>();
        services.RemoveAll<IBoundedDocumentStore>();
        services.TryAddSingleton<GroundworkDocumentStoreHolder>();

        // MongoDB does not yet advertise a bounded workflow-history page adapter. Remove a stale
        // relational provider registration when the host changes its selected provider leaf.
        services.RemoveAll<IGroundworkWorkflowExecutionStatePageQuery>();

        services.TryAddSingleton<IMongoDbGroundworkRuntimeAdmission, MongoDbGroundworkRuntimeAdmission>();
        services.AddSingleton(serviceProvider => new MongoDbGroundworkDocumentStoreInitializer(
            connectionString,
            databaseName,
            serviceProvider.GetRequiredService<GroundworkDocumentStoreHolder>(),
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            serviceProvider.GetRequiredService<IMongoDbGroundworkRuntimeAdmission>()));
        services.AddHostedService(serviceProvider =>
            serviceProvider.GetRequiredService<MongoDbGroundworkDocumentStoreInitializer>());
        services.AddSingleton<IShellInitializer>(serviceProvider =>
            serviceProvider.GetRequiredService<MongoDbGroundworkDocumentStoreInitializer>());
        services.AddSingleton(new ShellInitializerRegistration(
            typeof(MongoDbGroundworkDocumentStoreInitializer),
            LifecyclePhase.Prepare,
            Order: 0,
            RegistrationIndex: 0,
            IsExplicit: true,
            Source: $"{nameof(MongoDbGroundworkDocumentStoreRegistration)}.{nameof(AddMongoDbGroundworkDocumentStore)}"));
        services.AddSingleton<IDocumentStore>(serviceProvider =>
            serviceProvider.GetRequiredService<GroundworkDocumentStoreHolder>().Store);
        services.AddSingleton<IBoundedDocumentStore>(serviceProvider =>
            serviceProvider.GetRequiredService<GroundworkDocumentStoreHolder>().BoundedStore);
        return services;
    }
}
