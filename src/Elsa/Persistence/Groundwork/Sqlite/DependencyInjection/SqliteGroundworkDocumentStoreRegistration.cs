using CShells.Lifecycle;
using Groundwork.Documents.Store;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Unified.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Persistence.Groundwork.Sqlite.DependencyInjection;

/// <summary>
/// Shared wiring that registers SQLite-backed scoped Groundwork sessions for both the runtime-only and
/// unified provider registrations. A static provider factory is published after read-only schema admission by a
/// <see cref="SqliteGroundworkDocumentStoreInitializer"/> (run as both a hosted service and a CShells shell
/// initializer, in the <see cref="LifecyclePhase.Prepare"/> phase). Scoped <see cref="IDocumentStore"/> adapters
/// then acquire immutable access-bound sessions without retaining request state. The initializer never applies or repairs schema.
/// </summary>
public static class SqliteGroundworkDocumentStoreRegistration
{
    public static IServiceCollection AddSqliteGroundworkDocumentStore(
        this IServiceCollection services,
        string connectionString,
        bool autoApplyOnStartup = false,
        bool skipInspectionWhenPlanUnchanged = false,
        SqliteGroundworkStoreCacheOptions? storeCacheOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.SelectGroundworkProviderLeaf("sqlite");
        services.AddGroundworkStorageComposition();
        if (services.Any(descriptor => descriptor.ServiceType == typeof(SqliteGroundworkDocumentStoreInitializer)))
            return services;

        services.AddGroundworkStoreSessions();
        services.AddSingleton(sp => new SqliteGroundworkDocumentStoreInitializer(
            connectionString,
            autoApplyOnStartup,
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<GroundworkStoreSessionSource>(),
            // Provider composition is also used by source-only tooling hosts that intentionally omit logging.
            sp.GetService<ILogger<SqliteGroundworkDocumentStoreInitializer>>()
            ?? NullLogger<SqliteGroundworkDocumentStoreInitializer>.Instance,
            sp.GetRequiredService<Elsa.Persistence.Groundwork.Unified.Composition.GroundworkProviderCapabilityAdmission>(),
            skipInspectionWhenPlanUnchanged,
            storeCacheOptions));
        services.AddHostedService(sp => sp.GetRequiredService<SqliteGroundworkDocumentStoreInitializer>());
        services.AddSingleton<IShellInitializer>(sp => sp.GetRequiredService<SqliteGroundworkDocumentStoreInitializer>());
        services.AddSingleton(new ShellInitializerRegistration(
            typeof(SqliteGroundworkDocumentStoreInitializer),
            LifecyclePhase.Prepare,
            Order: 0,
            RegistrationIndex: -1,
            IsExplicit: true,
            Source: $"{nameof(SqliteGroundworkDocumentStoreRegistration)}.{nameof(AddSqliteGroundworkDocumentStore)}"));
        services.AddGroundworkSchemaReadinessGuard();
        return services;
    }
}
