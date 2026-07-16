using Elsa.Activities.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Foundation.Identity.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.PostgreSql.DependencyInjection;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.Unified.DependencyInjection;
using Elsa.Secrets.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Publishing.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.DependencyInjection;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.PostgreSql.Unified.DependencyInjection;

/// <summary>
/// Selects all seven Elsa Groundwork families (runtime, identity, secrets, distributed runtime,
/// workflows design, activities design and publishing) and exposes one PostgreSQL physical store for
/// their validated host-selected composition. Runtime startup admits the exact applied target; schema
/// application remains an operator/CLI responsibility.
/// </summary>
public static class GroundworkPostgreSqlUnifiedRegistration
{
    /// <param name="services">The host service collection.</param>
    /// <param name="connectionString">The PostgreSQL connection string the single document store opens.</param>
    /// <param name="autoApplyOnStartup">Apply safe pending schema operations at startup instead of throwing.</param>
    public static IServiceCollection AddGroundworkPostgreSqlUnifiedPersistence(this IServiceCollection services, string connectionString, bool autoApplyOnStartup = false)
    {
        services.AddGroundworkStorageComposition<GroundworkAllFeaturesDeploymentSchema>();
        services.AddPostgreSqlGroundworkDocumentStore(connectionString, autoApplyOnStartup);

        services.AddGroundworkRuntimeStores();
        services.AddGroundworkIdentityStores();
        services.AddGroundworkSecretsStore();
        services.AddGroundworkDistributedRuntimeStores();
        services.AddGroundworkWorkflowsDesignStores();
        services.AddGroundworkActivitiesDesignStores();
        services.AddGroundworkPublishingStores();

        return services;
    }
}
