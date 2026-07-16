using Elsa.Activities.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Foundation.Identity.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.SqlServer.DependencyInjection;
using Elsa.Persistence.Groundwork.Unified.DependencyInjection;
using Elsa.Secrets.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Publishing.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.SqlServer.Unified.DependencyInjection;

/// <summary>Registers all seven selected store families against one SQL Server Groundwork target.</summary>
public static class GroundworkSqlServerUnifiedRegistration
{
    public static IServiceCollection AddGroundworkSqlServerUnifiedPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddGroundworkStorageComposition<GroundworkAllFeaturesDeploymentSchema>();
        services.AddGroundworkRuntimeStores();
        services.AddGroundworkIdentityStores();
        services.AddGroundworkSecretsStore();
        services.AddGroundworkDistributedRuntimeStores();
        services.AddGroundworkWorkflowsDesignStores();
        services.AddGroundworkActivitiesDesignStores();
        services.AddGroundworkPublishingStores();
        services.AddSqlServerGroundworkDocumentStore(connectionString);
        return services;
    }
}
