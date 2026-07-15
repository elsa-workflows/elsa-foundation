using CShells.Lifecycle;
using Groundwork.Documents.Store;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Unified.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Elsa.Persistence.Groundwork.Sqlite.DependencyInjection;

/// <summary>
/// Shared wiring that registers the one SQLite-backed Groundwork <see cref="IDocumentStore"/> for both the
/// runtime-only and unified provider registrations. The store is created once after read-only schema admission by a
/// <see cref="SqliteGroundworkDocumentStoreInitializer"/> (run as both a hosted service and a CShells shell
/// initializer, in the <see cref="LifecyclePhase.Prepare"/> phase) which populates a shared
/// <see cref="GroundworkDocumentStoreHolder"/>; <see cref="IDocumentStore"/> resolves from that holder, so
/// consumers get a fully-initialized singleton with no synchronous block on the resolving thread. The initializer
/// never applies or repairs schema.
/// </summary>
public static class SqliteGroundworkDocumentStoreRegistration
{
    public static IServiceCollection AddSqliteGroundworkDocumentStore(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.SelectGroundworkProviderLeaf("sqlite");
        services.AddGroundworkStorageComposition();
        if (services.Any(descriptor => descriptor.ServiceType == typeof(SqliteGroundworkDocumentStoreInitializer)))
            return services;

        services.RemoveAll<IDocumentStore>();
        services.RemoveAll<IBoundedDocumentStore>();
        services.TryAddSingleton<GroundworkDocumentStoreHolder>();
        services.RemoveAll<IGroundworkWorkflowExecutionStatePageQuery>();
        services.AddSingleton<IGroundworkWorkflowExecutionStatePageQuery>(sp => new SqliteWorkflowExecutionStatePageQuery(
            connectionString,
            sp.GetRequiredService<GroundworkDocumentStoreHolder>(),
            sp.GetRequiredService<IGroundworkRuntimeDocumentSerializer>()));
        services.AddSingleton(sp => new SqliteGroundworkDocumentStoreInitializer(
            connectionString,
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp,
            sp.GetRequiredService<GroundworkDocumentStoreHolder>()));
        services.AddHostedService(sp => sp.GetRequiredService<SqliteGroundworkDocumentStoreInitializer>());
        services.AddSingleton<IShellInitializer>(sp => sp.GetRequiredService<SqliteGroundworkDocumentStoreInitializer>());
        services.AddSingleton(new ShellInitializerRegistration(
            typeof(SqliteGroundworkDocumentStoreInitializer),
            LifecyclePhase.Prepare,
            Order: 0,
            RegistrationIndex: 0,
            IsExplicit: true,
            Source: $"{nameof(SqliteGroundworkDocumentStoreRegistration)}.{nameof(AddSqliteGroundworkDocumentStore)}"));
        services.TryAddSingleton<IDocumentStore>(sp => sp.GetRequiredService<GroundworkDocumentStoreHolder>().Store);
        services.TryAddSingleton<IBoundedDocumentStore>(sp => sp.GetRequiredService<GroundworkDocumentStoreHolder>().BoundedStore);
        return services;
    }
}
