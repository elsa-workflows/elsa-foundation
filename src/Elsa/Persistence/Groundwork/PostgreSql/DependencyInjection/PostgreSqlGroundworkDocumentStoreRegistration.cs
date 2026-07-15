using CShells.Lifecycle;
using Groundwork.Documents.Store;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Unified.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Elsa.Persistence.Groundwork.PostgreSql.DependencyInjection;

/// <summary>
/// Shared wiring that registers the one PostgreSQL-backed Groundwork <see cref="IDocumentStore"/> for both the
/// runtime-only and unified provider registrations. The store is created once after read-only schema admission by a
/// <see cref="PostgreSqlGroundworkDocumentStoreInitializer"/> (run as both a hosted service and a CShells shell
/// initializer, in the <see cref="LifecyclePhase.Prepare"/> phase) which populates a shared
/// <see cref="GroundworkDocumentStoreHolder"/>; <see cref="IDocumentStore"/> resolves from that holder, so
/// consumers get a fully-initialized singleton with no synchronous block on the resolving thread. The initializer
/// never applies or repairs schema.
/// </summary>
public static class PostgreSqlGroundworkDocumentStoreRegistration
{
    public static IServiceCollection AddPostgreSqlGroundworkDocumentStore(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.SelectGroundworkProviderLeaf("postgresql");
        services.AddGroundworkStorageComposition();
        if (services.Any(descriptor => descriptor.ServiceType == typeof(PostgreSqlGroundworkDocumentStoreInitializer)))
            return services;

        services.RemoveAll<IDocumentStore>();
        services.RemoveAll<IBoundedDocumentStore>();
        services.TryAddSingleton<GroundworkDocumentStoreHolder>();
        services.RemoveAll<IGroundworkWorkflowExecutionStatePageQuery>();
        services.AddSingleton<IGroundworkWorkflowExecutionStatePageQuery>(sp => new PostgreSqlWorkflowExecutionStatePageQuery(
            connectionString,
            sp.GetRequiredService<GroundworkDocumentStoreHolder>(),
            sp.GetRequiredService<IGroundworkRuntimeDocumentSerializer>()));
        services.AddSingleton(sp => new PostgreSqlGroundworkDocumentStoreInitializer(
            connectionString,
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp,
            sp.GetRequiredService<GroundworkDocumentStoreHolder>()));
        services.AddHostedService(sp => sp.GetRequiredService<PostgreSqlGroundworkDocumentStoreInitializer>());
        services.AddSingleton<IShellInitializer>(sp => sp.GetRequiredService<PostgreSqlGroundworkDocumentStoreInitializer>());
        services.AddSingleton(new ShellInitializerRegistration(
            typeof(PostgreSqlGroundworkDocumentStoreInitializer),
            LifecyclePhase.Prepare,
            Order: 0,
            RegistrationIndex: 0,
            IsExplicit: true,
            Source: $"{nameof(PostgreSqlGroundworkDocumentStoreRegistration)}.{nameof(AddPostgreSqlGroundworkDocumentStore)}"));
        services.TryAddSingleton<IDocumentStore>(sp => sp.GetRequiredService<GroundworkDocumentStoreHolder>().Store);
        services.TryAddSingleton<IBoundedDocumentStore>(sp => sp.GetRequiredService<GroundworkDocumentStoreHolder>().BoundedStore);
        return services;
    }
}
