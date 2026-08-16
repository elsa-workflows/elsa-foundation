using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Targets;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Stores;
using Groundwork.Kernel;
using Groundwork.Store;
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
            provider.GetRequiredService<Elsa.Persistence.Core.IPersistenceAccessContextAccessor>(),
            targetName));
        services.RemoveAll<IExecutionCommandTransport>();
        services.AddScoped<IExecutionCommandTransport>(provider => new GroundworkExecutionCommandTransport(
            provider.GetRequiredService<IGroundworkStorageSessionSource>(),
            provider.GetRequiredService<Elsa.Persistence.Core.IPersistenceAccessContextAccessor>(),
            targetName));
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowDispatchDurabilityEvidence, GroundworkDistributionDurabilityEvidence>());
        services.Replace(ServiceDescriptor.Singleton<IWorkflowExecutionLeaseFencingCapability>(provider =>
            new GroundworkWorkflowExecutionLeaseFencingCapability(provider, targetName)));
        return services;
    }
}

internal sealed class GroundworkDistributionDurabilityEvidence : IWorkflowDispatchDurabilityEvidence
{
    public string Component => WorkflowDispatchDurabilityComponents.DistributionPersistence;
    public WorkflowDispatchDurabilityLevel Level => WorkflowDispatchDurabilityLevel.Durable;
}

/// <summary>Reports atomic-commit admission from the selected public Groundwork v2 connection.</summary>
public sealed class GroundworkWorkflowExecutionLeaseFencingCapability(IServiceProvider services, string? targetName) : IWorkflowExecutionLeaseFencingCapability
{
    public bool IsAvailable
    {
        get
        {
            var target = GroundworkTargetNames.Normalize(targetName);
            var connection = services.GetKeyedService<IStorageProviderConnection>(target) ??
                (GroundworkTargetNames.IsDefault(target) ? services.GetService<IStorageProviderConnection>() : null);
            return connection?.Capabilities.Any(capability => capability.Id == WellKnownCapabilities.AtomicCommit) == true;
        }
    }
}
