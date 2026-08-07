using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Stores;
using Groundwork.Core.Capabilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.DependencyInjection;

/// <summary>
/// Replaces the in-memory placement store and command transport registered by the distributed runtime feature with
/// the durable Groundwork-backed bridges, so placement leases and the cross-node command inbox survive process
/// restarts and are shared by every node through one host-selected document store. A provider feature is
/// responsible for registering the concrete <c>IDocumentStore</c> these bridges consume (materialized from a
/// manifest that includes <see cref="DistributedGroundworkStorageManifest"/>); this method only swaps the two leaf
/// store contracts over to the bridge implementations.
/// </summary>
public static class GroundworkDistributedStoresRegistration
{
    public static IServiceCollection AddGroundworkDistributedRuntimeStores(this IServiceCollection services)
    {
        services.TryAddSingleton<GroundworkProviderCapabilityAdmission>();
        services.AddGroundworkManifestSource<DistributedGroundworkStorageManifestSource>();

        // RemoveAll guarantees the bridge wins regardless of feature composition order (the distributed feature
        // registers its in-memory defaults with TryAddScoped, so bridge-first ordering also composes correctly).
        services.RemoveAll<IExecutionPlacementStore>();
        services.AddScoped<IExecutionPlacementStore, GroundworkExecutionPlacementStore>();
        services.RemoveAll<IExecutionCommandTransport>();
        services.AddScoped<IExecutionCommandTransport, GroundworkExecutionCommandTransport>();
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IWorkflowDispatchDurabilityEvidence, GroundworkDistributionDurabilityEvidence>());
        services.Replace(ServiceDescriptor.Singleton<IWorkflowExecutionLeaseFencingCapability, GroundworkWorkflowExecutionLeaseFencingCapability>());

        return services;
    }
}

internal sealed class GroundworkDistributionDurabilityEvidence : IWorkflowDispatchDurabilityEvidence
{
    public string Component => WorkflowDispatchDurabilityComponents.DistributionPersistence;
    public WorkflowDispatchDurabilityLevel Level => WorkflowDispatchDurabilityLevel.Durable;
}

/// <summary>
/// Maps the admitted Groundwork checkpoint-commit route to the provider-neutral actor capability.
/// It stays unavailable until the selected physical provider has published successful admission evidence.
/// </summary>
public sealed class GroundworkWorkflowExecutionLeaseFencingCapability(
    GroundworkProviderCapabilityAdmission admission) : IWorkflowExecutionLeaseFencingCapability
{
    private static readonly GroundworkActiveStoragePath CheckpointCommitPath = CreateCheckpointCommitPath();
    private static readonly GroundworkStorageTopologyRequirement CheckpointCommitTopology = new(
        RuntimeGroundworkStorageManifestSource.MultiDocumentTransactionsTopologyIdentity);

    public bool IsAvailable => admission.IsCapabilityAdmitted(
        CheckpointCommitPath,
        WellKnownCapabilities.AtomicCommit,
        CheckpointCommitTopology);

    private static GroundworkActiveStoragePath CreateCheckpointCommitPath()
    {
        var route = RuntimeGroundworkStorageManifestSource.CreateCheckpointCommitRouteRequirement();
        return new GroundworkActiveStoragePath(
            RuntimeGroundworkStorageManifestSource.FeatureName,
            route.StorageUnit,
            route.RouteIdentity,
            route.RequiredCapabilities);
    }
}
