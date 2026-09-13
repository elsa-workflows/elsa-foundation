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
        var existingBackend = ExecutionPlacementStoreBackend.Find(services);
        if (existingBackend is not null)
            existingBackend.EnsureOwnsRegisteredContract(services);
        else if (ExecutionPlacementStoreBackend.HasRegisteredContract(services))
            throw new InvalidOperationException("An explicit IExecutionPlacementStore is already registered; Groundwork placement persistence refuses to replace it implicitly.");
        var existingCommandBackend = ExecutionCommandTransportBackend.Find(services);
        existingCommandBackend?.EnsureOwnsRegisteredContract(services);
        if (ExecutionCommandTransportBackend.HasRegisteredContract(services) && existingCommandBackend is null)
            throw new InvalidOperationException("An explicit IExecutionCommandTransport is already registered; Groundwork command transport persistence refuses to replace it implicitly.");

        if (!string.Equals(existingBackend?.Name, ExecutionPlacementStoreBackend.EntityFramework, StringComparison.Ordinal))
            services.AddGroundworkStorageUnit(DistributedGroundworkStorageManifest.CreatePlacementUnit(), targetName);

        if (!string.Equals(existingBackend?.Name, ExecutionPlacementStoreBackend.EntityFramework, StringComparison.Ordinal))
        {
            services.RemoveAll<ExecutionPlacementStoreBackend>();
            services.RemoveAll<IExecutionPlacementStore>();
            var placementDescriptor = ServiceDescriptor.Scoped<IExecutionPlacementStore>(provider => new GroundworkExecutionPlacementStore(
                provider.GetRequiredService<IGroundworkStorageSessionSource>(),
                provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
                targetName));
            services.Add(placementDescriptor);
            ExecutionPlacementStoreBackend.Register(
                services,
                new ExecutionPlacementStoreBackend(
                    ExecutionPlacementStoreBackend.Groundwork,
                    placementDescriptor,
                    collection => collection.RemoveGroundworkStorageUnit(DistributedGroundworkStorageManifest.PlacementUnitId)));
        }
        if (!string.Equals(existingCommandBackend?.Name, ExecutionCommandTransportBackend.EntityFramework, StringComparison.Ordinal))
        {
            existingCommandBackend?.RemoveOwnedArtifacts(services);
            services.AddGroundworkStorageUnit(DistributedGroundworkStorageManifest.CreateCommandStreamHeadUnit(), targetName);
            services.AddGroundworkStorageUnit(DistributedGroundworkStorageManifest.CreateCommandTransportUnit(), targetName);
            services.RemoveAll<ExecutionCommandTransportBackend>();
            services.RemoveAll<IExecutionCommandTransport>();
            var commandDescriptor = ServiceDescriptor.Scoped<IExecutionCommandTransport>(provider => new GroundworkExecutionCommandTransport(
                provider.GetRequiredService<IGroundworkStorageSessionSource>(),
                provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
                targetName));
            services.Add(commandDescriptor);
            var durabilityEvidenceDescriptor = ServiceDescriptor.Scoped<IWorkflowDispatchDurabilityEvidence, GroundworkDistributionDurabilityEvidence>();
            ExecutionCommandTransportBackend.Register(
                services,
                new ExecutionCommandTransportBackend(
                    ExecutionCommandTransportBackend.Groundwork,
                    commandDescriptor,
                    collection =>
                    {
                        collection.RemoveGroundworkStorageUnit(DistributedGroundworkStorageManifest.CommandStreamHeadUnitId);
                        collection.RemoveGroundworkStorageUnit(DistributedGroundworkStorageManifest.CommandTransportUnitId);
                        collection.Remove(durabilityEvidenceDescriptor);
                    }));
            services.Add(durabilityEvidenceDescriptor);
            services.Replace(ServiceDescriptor.Singleton<IWorkflowExecutionLeaseFencingCapability>(
                GroundworkDistributionLeaseFencingCapability.Instance));
        }
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
