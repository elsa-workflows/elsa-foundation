using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Stores;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Workflows.Runtime.Distributed.Tests;

/// <summary>
/// The W20 acceptance suite over the durable Groundwork-backed stores (W27): both nodes share ONE document store —
/// the cluster's durable state — through the placement and transport bridges. Same scenarios, same assertions:
/// cross-node routing drains in order exactly once, and a node killed mid-drain is re-driven on the survivor while
/// the dead node's late commit is fenced by W5. This is the production-shaped cluster state; the in-memory variant
/// remains the single-process baseline.
/// </summary>
public sealed class GroundworkTwoNodeAcceptanceTests : TwoNodeAcceptanceTests
{
    protected override ClusterState CreateClusterState()
    {
        var runtimeManifest = ElsaRuntimeStorageManifest.Create();
        var distributedManifest = DistributedGroundworkStorageManifest.Create();
        var combinedManifest = runtimeManifest with
        {
            StorageUnits = Array.AsReadOnly(
                runtimeManifest.StorageUnits.Concat(distributedManifest.StorageUnits).ToArray())
        };
        var documentStore = new InMemoryDocumentStore(combinedManifest);
        var dispatchAccess = new FixedAccessContextAccessor("tenant-distributed");
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IDocumentStore>(documentStore);
        services.AddSingleton<IBoundedDocumentStore>(documentStore);
        services.AddSingleton<IPersistenceAccessContextAccessor>(dispatchAccess);
        services.AddSingleton<IWorkflowExecutableRootWriteLeaseManager>(PassThroughRootWriteLeaseManager.Instance);
        services.AddGroundworkRuntimeStores();
        var provider = services.BuildServiceProvider();

        DispatchPersistence OpenPersistence()
        {
            var scope = provider.CreateScope();
            return new DispatchPersistence(
                scope.ServiceProvider.GetRequiredService<IRuntimeCheckpointCommitStore>(),
                scope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxStore>(),
                scope.ServiceProvider.GetRequiredService<IWorkflowDispatchStore>(),
                scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>(),
                scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>(),
                scope.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>(),
                dispatchAccess,
                scope);
        }

        return new ClusterState(
            new GroundworkExecutionPlacementStore(documentStore),
            new GroundworkExecutionCommandTransport(documentStore, new DefaultAccessContextAccessor()),
            provider.GetRequiredService<IExecutionLivenessStateStore>(),
            OpenPersistence);
    }

    private sealed class DefaultAccessContextAccessor : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } =
            PersistenceAccessContext.Scoped(new PersistenceScope(PersistenceScope.DefaultValue));
    }

}
