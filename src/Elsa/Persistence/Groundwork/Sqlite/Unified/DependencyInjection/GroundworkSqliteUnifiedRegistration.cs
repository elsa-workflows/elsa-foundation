using Elsa.Activities.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Sqlite.DependencyInjection;
using Elsa.Persistence.Groundwork.Unified;
using Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Publishing.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Capabilities;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.Sqlite.Unified.DependencyInjection;

/// <summary>
/// Registers a single SQLite-backed Groundwork <see cref="IDocumentStore"/> — materialized from the unioned
/// runtime + workflows-design + activities-design + workflows-publishing manifest (<see cref="GroundworkUnifiedManifest"/>) — and points
/// every Elsa persistence lane's read/write ports at it. This is the concrete realization of the host-selects-
/// the-provider goal: domain and runtime code reference only the neutral ports, and one host choice (SQLite here)
/// backs every module from one database.
/// </summary>
public static class GroundworkSqliteUnifiedRegistration
{
    /// <param name="services">The host service collection.</param>
    /// <param name="connectionString">The SQLite connection string the single document store opens.</param>
    public static IServiceCollection AddGroundworkSqliteUnifiedPersistence(this IServiceCollection services, string connectionString) =>
        services.AddGroundworkSqliteUnifiedPersistence(connectionString, new WorkflowExecutableCacheOptions { Enabled = false });

    /// <summary>Registers unified SQLite persistence with explicit executable-cache options.</summary>
    public static IServiceCollection AddGroundworkSqliteUnifiedPersistence(
        this IServiceCollection services,
        string connectionString,
        WorkflowExecutableCacheOptions workflowExecutableCacheOptions)
    {
        ArgumentNullException.ThrowIfNull(workflowExecutableCacheOptions);

        // One store, one database, one materialized union manifest — shared by every lane.
        services.AddSqliteGroundworkDocumentStore(
            connectionString,
            GroundworkUnifiedManifest.Create(),
            new ProviderIdentity("groundwork-sqlite", "1.0.0"));

        services.AddGroundworkRuntimeStores(workflowExecutableCacheOptions);
        services.AddGroundworkWorkflowsDesignStores();
        services.AddGroundworkActivitiesDesignStores();
        services.AddGroundworkPublishingStores();

        return services;
    }
}
