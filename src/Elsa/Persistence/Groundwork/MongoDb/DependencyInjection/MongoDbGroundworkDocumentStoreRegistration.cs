using CShells.Lifecycle;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Unified.DependencyInjection;
using Elsa.Persistence.Groundwork.Scoping;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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

        services.TryAddSingleton<IMongoDbGroundworkRuntimeAdmission, MongoDbGroundworkRuntimeAdmission>();
        services.AddSingleton(serviceProvider => new MongoDbGroundworkDocumentStoreInitializer(
            connectionString,
            databaseName,
            autoApplyOnStartup,
            serviceProvider.GetRequiredService<GroundworkStoreSessionSource>(),
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            serviceProvider.GetRequiredService<IMongoDbGroundworkRuntimeAdmission>(),
            // Provider composition is also used by source-only tooling hosts that intentionally omit logging.
            serviceProvider.GetService<ILogger<MongoDbGroundworkDocumentStoreInitializer>>()
            ?? NullLogger<MongoDbGroundworkDocumentStoreInitializer>.Instance,
            serviceProvider.GetRequiredService<Elsa.Persistence.Groundwork.Unified.Composition.GroundworkProviderCapabilityAdmission>()));
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
        services.AddGroundworkSchemaReadinessGuard();
        return services;
    }
}
