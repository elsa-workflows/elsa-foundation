using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Stores;

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
    protected override (IExecutionPlacementStore PlacementStore, IExecutionCommandTransport Transport) CreateClusterState()
    {
        var documentStore = new InMemoryDocumentStore(DistributedGroundworkStorageManifest.Create());
        return (
            new GroundworkExecutionPlacementStore(documentStore),
            new GroundworkExecutionCommandTransport(documentStore, new DefaultAccessContextAccessor()));
    }

    private sealed class DefaultAccessContextAccessor : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } =
            PersistenceAccessContext.Scoped(new PersistenceScope(PersistenceScope.DefaultValue));
    }
}
