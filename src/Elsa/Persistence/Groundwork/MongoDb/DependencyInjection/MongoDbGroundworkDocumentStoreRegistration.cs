using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Targets;
using Elsa.Persistence.Groundwork.Unified.DependencyInjection;
using Elsa.Persistence.Groundwork.Scoping;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Persistence.Groundwork.MongoDb.DependencyInjection;

/// <summary>
/// Declares one MongoDB-backed Groundwork target. The document store remains unavailable until the startup
/// admission has observed a transaction-capable replica set and the exact deployment-owned schema.
/// </summary>
public static class MongoDbGroundworkDocumentStoreRegistration
{
    public static IServiceCollection AddMongoDbGroundworkDocumentStore(
        this IServiceCollection services,
        string connectionString,
        string databaseName,
        bool autoApplyOnStartup = false,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        var target = GroundworkTargetNames.Normalize(targetName);
        services.AddGroundworkStorageComposition();

        // The database name is part of what the target addresses, so it participates in the declaration
        // fingerprint. Two targets against one cluster but different databases are distinct stores.
        if (services.DeclareGroundworkTarget(target, ProviderIdentity, $"{connectionString}|{databaseName}")
            is GroundworkTargetDeclarationResult.AlreadyDeclared)
        {
            return services;
        }

        services.AddGroundworkStoreSessions(target);

        services.AddGroundworkProviderCapabilityAdmission(target);

        services.TryAddSingleton<IMongoDbGroundworkRuntimeAdmission, MongoDbGroundworkRuntimeAdmission>();
        services.AddKeyedSingleton(target, (serviceProvider, key) => new MongoDbGroundworkDocumentStoreInitializer(
            (string)key,
            connectionString,
            databaseName,
            autoApplyOnStartup,
            serviceProvider.GetRequiredKeyedService<GroundworkStoreSessionSource>(key),
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            serviceProvider.GetRequiredService<IMongoDbGroundworkRuntimeAdmission>(),
            // Provider composition is also used by source-only tooling hosts that intentionally omit logging.
            serviceProvider.GetService<ILogger<MongoDbGroundworkDocumentStoreInitializer>>()
            ?? NullLogger<MongoDbGroundworkDocumentStoreInitializer>.Instance,
            serviceProvider.GetRequiredKeyedService<Elsa.Persistence.Groundwork.Unified.Composition.GroundworkProviderCapabilityAdmission>(key)));
        services.AddGroundworkTargetAdmission<MongoDbGroundworkDocumentStoreInitializer>(target);
        services.AddGroundworkSchemaReadinessGuard();
        return services;
    }

    /// <summary>The concrete Groundwork provider leaf identity recorded on every target this leaf declares.</summary>
    public const string ProviderIdentity = "mongodb";
}
