using Elsa.Activities.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Foundation.Identity.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.MongoDb.DependencyInjection;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.Unified.DependencyInjection;
using Elsa.Secrets.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Publishing.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.MongoDb.Unified.DependencyInjection;

/// <summary>Materializes the host-selected Elsa store families through one MongoDB provider target.</summary>
public static class GroundworkMongoDbUnifiedRegistration
{
    public static IServiceCollection AddGroundworkMongoDbUnifiedPersistence(
        this IServiceCollection services,
        string connectionString,
        string databaseName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        services.AddGroundworkStorageComposition<GroundworkAllFeaturesDeploymentSchema>();
        services.AddGroundworkRuntimeStores();
        services.AddGroundworkIdentityStores();
        services.AddGroundworkSecretsStore();
        services.AddGroundworkDistributedRuntimeStores();
        services.AddGroundworkWorkflowsDesignStores();
        services.AddGroundworkActivitiesDesignStores();
        services.AddGroundworkPublishingStores();
        services.AddMongoDbGroundworkDocumentStore(connectionString, databaseName);
        return services;
    }
}
