using Elsa.Activities.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Sqlite;
using Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection;
using Groundwork.Core.Capabilities;
using Groundwork.Documents.Store;
using Groundwork.Sqlite.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Persistence.Groundwork.Sqlite.Unified.DependencyInjection;

/// <summary>
/// Registers a single SQLite-backed Groundwork <see cref="IDocumentStore"/> — materialized from the unioned
/// runtime + workflows-design + activities-design manifest (<see cref="GroundworkUnifiedManifest"/>) — and points
/// every Elsa persistence lane's read/write ports at it. This is the concrete realization of the host-selects-
/// the-provider goal: domain and runtime code reference only the neutral ports, and one host choice (SQLite here)
/// backs every module from one database.
/// </summary>
public static class GroundworkSqliteUnifiedRegistration
{
    /// <param name="services">The host service collection.</param>
    /// <param name="connectionString">The SQLite connection string the single document store opens.</param>
    public static IServiceCollection AddGroundworkSqliteUnifiedPersistence(this IServiceCollection services, string connectionString)
    {
        // One store, one database, one materialized union manifest — shared by every lane.
        services.RemoveAll<IDocumentStore>();
        services.AddSingleton(_ => SqliteDocumentStoreFactory
            .CreateAsync(
                connectionString,
                GroundworkUnifiedManifest.Create(),
                new ProviderIdentity("groundwork-sqlite", "1.0.0"))
            .GetAwaiter()
            .GetResult());
        services.AddSingleton<IDocumentStore>(provider =>
            provider.GetRequiredService<SqliteDocumentStoreHandle>().Store);

        services.AddGroundworkRuntimeStores();
        services.AddGroundworkWorkflowsDesignStores();
        services.AddGroundworkActivitiesDesignStores();

        return services;
    }
}
