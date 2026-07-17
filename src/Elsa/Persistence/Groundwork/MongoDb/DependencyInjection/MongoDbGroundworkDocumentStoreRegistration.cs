using CShells.Lifecycle;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Scoping;
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
        string databaseName,
        bool autoApplyOnStartup = false)
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
        services.AddGroundworkStoreSessions();

        // MongoDB does not yet advertise a bounded workflow-history page adapter. Remove a stale
        // relational provider registration when the host changes its selected provider leaf.
        services.RemoveAll<IGroundworkWorkflowExecutionStatePageQuery>();

        services.TryAddSingleton<IMongoDbGroundworkRuntimeAdmission, MongoDbGroundworkRuntimeAdmission>();
        services.AddSingleton(serviceProvider => new MongoDbGroundworkDocumentStoreInitializer(
            connectionString,
            databaseName,
            autoApplyOnStartup,
            serviceProvider.GetRequiredService<GroundworkStoreSessionSource>(),
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            serviceProvider.GetRequiredService<IMongoDbGroundworkRuntimeAdmission>(),
            serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MongoDbGroundworkDocumentStoreInitializer>>()));
        services.AddHostedService(serviceProvider =>
            serviceProvider.GetRequiredService<MongoDbGroundworkDocumentStoreInitializer>());
        services.AddSingleton<IShellInitializer>(serviceProvider =>
            serviceProvider.GetRequiredService<MongoDbGroundworkDocumentStoreInitializer>());
        services.AddSingleton(new ShellInitializerRegistration(
            typeof(MongoDbGroundworkDocumentStoreInitializer),
            LifecyclePhase.Prepare,
            Order: 0,
            RegistrationIndex: -1,
            IsExplicit: true,
            Source: $"{nameof(MongoDbGroundworkDocumentStoreRegistration)}.{nameof(AddMongoDbGroundworkDocumentStore)}"));
        return services;
    }
}
