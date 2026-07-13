using Elsa.Activities.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.PostgreSql.DependencyInjection;
using Elsa.Persistence.Groundwork.Unified;
using Elsa.Secrets.Persistence.Groundwork.DependencyInjection;
using Elsa.Studio.Preferences.Persistence.Groundwork;
using Elsa.Workflows.Dashboard.Persistence.Groundwork;
using Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection;
using Groundwork.Core.Capabilities;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.PostgreSql.Unified.DependencyInjection;

/// <summary>
/// Registers a single PostgreSQL-backed Groundwork <see cref="IDocumentStore"/> — materialized from the unioned
/// runtime + workflows-design + activities-design manifest (<see cref="GroundworkUnifiedManifest"/>) — and points
/// every Elsa persistence lane's read/write ports at it. This is the concrete realization of the host-selects-
/// the-provider goal: domain and runtime code reference only the neutral ports, and one host choice (PostgreSQL
/// here) backs every module from one database.
/// </summary>
public static class GroundworkPostgreSqlUnifiedRegistration
{
    /// <param name="services">The host service collection.</param>
    /// <param name="connectionString">The PostgreSQL connection string the single document store opens.</param>
    public static IServiceCollection AddGroundworkPostgreSqlUnifiedPersistence(this IServiceCollection services, string connectionString)
    {
        // One store, one database, one materialized union manifest — shared by every lane.
        services.AddPostgreSqlGroundworkDocumentStore(
            connectionString,
            GroundworkUnifiedManifest.Create(),
            new ProviderIdentity("groundwork-postgresql", "1.0.0"));

        services.AddGroundworkRuntimeStores();
        services.AddGroundworkWorkflowsDesignStores();
        services.AddGroundworkActivitiesDesignStores();
        services.AddGroundworkSecretsStore();
        services.AddGroundworkStudioPreferences();
        services.AddGroundworkWorkflowRunHealth(
            _ => new Npgsql.NpgsqlConnection(connectionString),
            GroundworkRunHealthDialect.PostgreSql);
        services.AddGroundworkWorkflowPortfolio(
            _ => new Npgsql.NpgsqlConnection(connectionString),
            GroundworkRunHealthDialect.PostgreSql);

        return services;
    }
}
