using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Workflows.Dashboard.Persistence.Groundwork.V2;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.PostgreSql;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.PostgreSql.Unified.DependencyInjection;

/// <summary>
/// Backs every provider-level Elsa store family — runtime, distributed runtime, workflows design,
/// activities design and publishing — with one PostgreSQL Groundwork target. Identity is selected
/// explicitly by its own feature.
/// </summary>
/// <remarks>
/// The storage session source admits each lane's declared units against this connection when the host
/// starts, so there is nothing for a caller to schedule or opt out of.
/// </remarks>
public static class GroundworkPostgreSqlUnifiedRegistration
{
    /// <param name="services">The host service collection.</param>
    /// <param name="connectionString">The PostgreSQL connection string the single provider connection opens.</param>
    public static IServiceCollection AddGroundworkPostgreSqlUnifiedPersistence(
        this IServiceCollection services,
        string connectionString) =>
        services.AddGroundworkPostgreSqlUnifiedPersistence(connectionString, new WorkflowExecutableCacheOptions());

    /// <summary>Registers unified PostgreSQL persistence with explicit executable-cache options.</summary>
    public static IServiceCollection AddGroundworkPostgreSqlUnifiedPersistence(
        this IServiceCollection services,
        string connectionString,
        WorkflowExecutableCacheOptions workflowExecutableCacheOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(workflowExecutableCacheOptions);

        services.AddGroundworkStorageProviderConnection(
            _ => new PostgreSqlProviderFactory().Create(connectionString));
        services.AddGroundworkUnifiedStoreFamilies(workflowExecutableCacheOptions);
        // Both tiles read the v2 lanes through the storage session source, so they need no connection
        // of their own and follow whatever target each lane is bound to.
        services.AddGroundworkV2WorkflowRunHealth();
        services.AddGroundworkV2WorkflowPortfolio();
        return services;
    }
}
