using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Workflows.Dashboard.Persistence.Groundwork.V2;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.Sqlite.Unified.DependencyInjection;

/// <summary>
/// Backs every provider-level Elsa store family — runtime, distributed runtime, workflows design,
/// activities design and publishing — with one SQLite Groundwork target. Identity is selected
/// explicitly by its own feature.
/// </summary>
/// <remarks>
/// The storage session source admits each lane's declared units against this connection when the host
/// starts, so there is nothing for a caller to schedule or opt out of.
/// </remarks>
public static class GroundworkSqliteUnifiedRegistration
{
    /// <param name="services">The host service collection.</param>
    /// <param name="connectionString">The SQLite connection string the single provider connection opens.</param>
    public static IServiceCollection AddGroundworkSqliteUnifiedPersistence(
        this IServiceCollection services,
        string connectionString) =>
        services.AddGroundworkSqliteUnifiedPersistence(connectionString, new WorkflowExecutableCacheOptions());

    /// <summary>Registers unified SQLite persistence with explicit executable-cache options.</summary>
    public static IServiceCollection AddGroundworkSqliteUnifiedPersistence(
        this IServiceCollection services,
        string connectionString,
        WorkflowExecutableCacheOptions workflowExecutableCacheOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(workflowExecutableCacheOptions);

        services.AddGroundworkStorageProviderConnection(
            _ => new SqliteProviderFactory().Create(connectionString));
        services.AddGroundworkUnifiedStoreFamilies(workflowExecutableCacheOptions);
        // Both tiles read the v2 lanes through the storage session source, so they need no connection
        // of their own and follow whatever target each lane is bound to.
        services.AddGroundworkV2WorkflowRunHealth();
        services.AddGroundworkV2WorkflowPortfolio();
        return services;
    }
}
