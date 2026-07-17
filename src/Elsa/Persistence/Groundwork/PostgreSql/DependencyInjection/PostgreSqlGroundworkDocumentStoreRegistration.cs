using CShells.Lifecycle;
using Groundwork.Documents.Store;
using Elsa.Persistence.Groundwork.Querying;
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

namespace Elsa.Persistence.Groundwork.PostgreSql.DependencyInjection;

/// <summary>
/// Shared wiring that registers PostgreSQL-backed scoped Groundwork sessions for both the runtime-only and
/// unified provider registrations. A static provider factory is published after read-only schema admission by a
/// <see cref="PostgreSqlGroundworkDocumentStoreInitializer"/> (run as both a hosted service and a CShells shell
/// initializer, in the <see cref="LifecyclePhase.Prepare"/> phase). Scoped <see cref="IDocumentStore"/> adapters
/// then acquire immutable access-bound sessions without retaining request state. The initializer never applies or repairs schema.
/// </summary>
public static class PostgreSqlGroundworkDocumentStoreRegistration
{
    public static IServiceCollection AddPostgreSqlGroundworkDocumentStore(
        this IServiceCollection services,
        string connectionString,
        bool autoApplyOnStartup = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.SelectGroundworkProviderLeaf("postgresql");
        services.AddGroundworkStorageComposition();
        if (services.Any(descriptor => descriptor.ServiceType == typeof(PostgreSqlGroundworkDocumentStoreInitializer)))
            return services;

        services.AddGroundworkStoreSessions();
        services.RemoveAll<IGroundworkWorkflowExecutionStatePageQuery>();
        services.AddScoped<IGroundworkWorkflowExecutionStatePageQuery>(sp => new PostgreSqlWorkflowExecutionStatePageQuery(
            connectionString,
            sp.GetRequiredService<IGroundworkRuntimeDocumentSerializer>(),
            sp.GetRequiredService<IPersistenceAccessContextAccessor>(),
            sp.GetRequiredService<GroundworkWorkflowExecutionStatePageRouteSource>()));
        services.AddSingleton(sp => new PostgreSqlGroundworkDocumentStoreInitializer(
            connectionString,
            autoApplyOnStartup,
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<GroundworkStoreSessionSource>(),
            // Provider composition is also used by source-only tooling hosts that intentionally omit logging.
            sp.GetService<ILogger<PostgreSqlGroundworkDocumentStoreInitializer>>()
            ?? NullLogger<PostgreSqlGroundworkDocumentStoreInitializer>.Instance));
        services.AddHostedService(sp => sp.GetRequiredService<PostgreSqlGroundworkDocumentStoreInitializer>());
        services.AddSingleton<IShellInitializer>(sp => sp.GetRequiredService<PostgreSqlGroundworkDocumentStoreInitializer>());
        services.AddSingleton(new ShellInitializerRegistration(
            typeof(PostgreSqlGroundworkDocumentStoreInitializer),
            LifecyclePhase.Prepare,
            Order: 0,
            RegistrationIndex: -1,
            IsExplicit: true,
            Source: $"{nameof(PostgreSqlGroundworkDocumentStoreRegistration)}.{nameof(AddPostgreSqlGroundworkDocumentStore)}"));
        return services;
    }
}
