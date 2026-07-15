using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Stores;
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
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IGroundworkStorageManifestSource, DistributedGroundworkStorageManifestSource>());

        // RemoveAll guarantees the bridge wins regardless of feature composition order (the distributed feature
        // registers its in-memory defaults with TryAddScoped, so bridge-first ordering also composes correctly).
        services.RemoveAll<IExecutionPlacementStore>();
        services.AddScoped<IExecutionPlacementStore, GroundworkExecutionPlacementStore>();
        services.RemoveAll<IExecutionCommandTransport>();
        services.AddScoped<IExecutionCommandTransport, GroundworkExecutionCommandTransport>();

        return services;
    }
}
