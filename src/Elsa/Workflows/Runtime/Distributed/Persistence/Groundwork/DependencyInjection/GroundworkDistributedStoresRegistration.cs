using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.DependencyInjection;

/// <summary>Registers the distributed runtime's pure Groundwork v2 store family.</summary>
public static class GroundworkDistributedStoresRegistration
{
    public static IServiceCollection AddGroundworkDistributedRuntimeStores(this IServiceCollection services, string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        foreach (var unit in DistributedGroundworkStorageManifest.CreateUnits())
            services.AddGroundworkStorageUnit(unit, targetName);

        services.RemoveAll<IExecutionPlacementStore>();
        services.AddScoped<IExecutionPlacementStore>(provider => new GroundworkExecutionPlacementStore(
            provider.GetRequiredService<IGroundworkStorageSessionSource>(),
            provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
            targetName));
        services.RemoveAll<IExecutionCommandTransport>();
        services.AddScoped<IExecutionCommandTransport>(provider => new GroundworkExecutionCommandTransport(
            provider.GetRequiredService<IGroundworkStorageSessionSource>(),
            provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
            targetName));
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowDispatchDurabilityEvidence, GroundworkDistributionDurabilityEvidence>());
        services.Replace(ServiceDescriptor.Singleton<IWorkflowExecutionLeaseFencingCapability>(
            GroundworkDistributionLeaseFencingCapability.Instance));
        return services;
    }
}

internal sealed class GroundworkDistributionDurabilityEvidence : IWorkflowDispatchDurabilityEvidence
{
    public string Component => WorkflowDispatchDurabilityComponents.DistributionPersistence;
    public WorkflowDispatchDurabilityLevel Level => WorkflowDispatchDurabilityLevel.Durable;
}

/// <summary>
/// The current v2 slice persists routing and transport only. Lease fencing stays unavailable until
/// the active checkpoint commit path is admitted on the same provider-owned v2 connection.
/// </summary>
internal sealed class GroundworkDistributionLeaseFencingCapability : IWorkflowExecutionLeaseFencingCapability
{
    public static GroundworkDistributionLeaseFencingCapability Instance { get; } = new();

    public bool IsAvailable => false;
}
