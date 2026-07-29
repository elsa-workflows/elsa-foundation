using Elsa.Activities.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Secrets.Persistence.Groundwork.DependencyInjection;
using Elsa.Studio.Preferences.Persistence.Groundwork;
using Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Publishing.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.ReferenceComposition;

/// <summary>Registers the six provider-neutral store families in the shipped reference composition.</summary>
public static class GroundworkUnifiedStoreFamiliesRegistration
{
    public static IServiceCollection AddGroundworkUnifiedStoreFamilies(this IServiceCollection services)
        => services.AddGroundworkUnifiedStoreFamilies(
            new WorkflowExecutableCacheOptions());

    public static IServiceCollection AddGroundworkUnifiedStoreFamilies(
        this IServiceCollection services,
        WorkflowExecutableCacheOptions workflowExecutableCacheOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(workflowExecutableCacheOptions);

        services.AddGroundworkRuntimeStores(workflowExecutableCacheOptions);
        services.AddGroundworkSecretsStore();
        services.AddGroundworkStudioPreferences();
        services.AddGroundworkDistributedRuntimeStores();
        services.AddGroundworkWorkflowsDesignStores();
        services.AddGroundworkActivitiesDesignStores();
        services.AddGroundworkPublishingStores();
        return services;
    }
}
