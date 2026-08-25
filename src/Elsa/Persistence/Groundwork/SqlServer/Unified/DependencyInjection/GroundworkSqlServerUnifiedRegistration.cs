using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Workflows.Dashboard.Persistence.Groundwork.V2;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.SqlServer;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.SqlServer.Unified.DependencyInjection;

/// <summary>
/// Backs every provider-level Elsa store family — runtime, distributed runtime, workflows design,
/// activities design and publishing — with one SQL Server Groundwork target. Identity is selected
/// explicitly by its own feature.
/// </summary>
/// <remarks>
/// The storage session source admits each lane's declared units against this connection when the host
/// starts, so there is nothing for a caller to schedule or opt out of.
/// </remarks>
public static class GroundworkSqlServerUnifiedRegistration
{
    /// <param name="services">The host service collection.</param>
    /// <param name="connectionString">The SQL Server connection string the single provider connection opens.</param>
    public static IServiceCollection AddGroundworkSqlServerUnifiedPersistence(
        this IServiceCollection services,
        string connectionString) =>
        services.AddGroundworkSqlServerUnifiedPersistence(connectionString, new WorkflowExecutableCacheOptions());

    /// <summary>Registers unified SQL Server persistence with explicit executable-cache options.</summary>
    public static IServiceCollection AddGroundworkSqlServerUnifiedPersistence(
        this IServiceCollection services,
        string connectionString,
        WorkflowExecutableCacheOptions workflowExecutableCacheOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(workflowExecutableCacheOptions);

        services.AddGroundworkStorageProviderConnection(
            _ => new SqlServerProviderFactory().Create(connectionString));
        services.AddGroundworkUnifiedStoreFamilies(workflowExecutableCacheOptions);
        // Both tiles read the v2 lanes through the storage session source, so they need no connection
        // of their own and follow whatever target each lane is bound to.
        services.AddGroundworkV2WorkflowRunHealth();
        services.AddGroundworkV2WorkflowPortfolio();
        return services;
    }
}
