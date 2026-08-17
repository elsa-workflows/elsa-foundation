using Elsa.Activities.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Design.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Publishing.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.ReferenceComposition;

/// <summary>
/// Binds every provider-neutral store family to one Groundwork target.
/// <para>
/// This is the shipped convenience preset, not a distinct composition: it calls the same per-lane
/// registrations a host would call itself, all naming one target. A host that wants lanes in different
/// databases enables the per-lane features instead and names a target on each.
/// </para>
/// <para>
/// The target is left unspecified rather than forced to <c>default</c>, so a host that composes this preset
/// and then names a target for one of these lanes still gets its way.
/// </para>
/// </summary>
public static class GroundworkUnifiedStoreFamiliesRegistration
{
    public static IServiceCollection AddGroundworkUnifiedStoreFamilies(
        this IServiceCollection services,
        string? targetName = null)
        => services.AddGroundworkUnifiedStoreFamilies(new WorkflowExecutableCacheOptions(), targetName);

    public static IServiceCollection AddGroundworkUnifiedStoreFamilies(
        this IServiceCollection services,
        WorkflowExecutableCacheOptions workflowExecutableCacheOptions,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(workflowExecutableCacheOptions);

        services.AddGroundworkRuntimeStores(workflowExecutableCacheOptions, targetName);
        // Distributed runtime has crossed the clean-break boundary to the public v2 store. Do not
        // compose it into this legacy document-store preset: doing so creates a mixed provider graph
        // and starts v2 schema admission without a v2 provider connection. Its public-v2 feature owns
        // that dependency-closed registration until the shipping host makes the atomic v2 switch.
        services.AddGroundworkWorkflowsDesignStores(targetName);
        services.AddGroundworkActivitiesDesignStores(targetName);
        services.AddGroundworkPublishingStores(targetName);
        return services;
    }
}
