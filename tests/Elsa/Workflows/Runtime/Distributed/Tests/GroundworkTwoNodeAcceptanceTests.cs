using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Stores;
using Groundwork.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Tests;

/// <summary>
/// The two-node acceptance suite with placement and command transport persisted through the public Groundwork v2
/// runtime. Both nodes and every simulated restart reopen the same provider-owned database; no v1 document store,
/// compatibility bridge, or process-local transport state participates in the proof.
/// </summary>
/// <remarks>
/// Checkpoint, outbox, dispatch, and execution-state restart proof remains explicitly pending until that shared-runtime
/// family is cut over to public Groundwork v2. The routing/failover scenarios below do not claim that separate boundary.
/// </remarks>
public sealed class GroundworkTwoNodeAcceptanceTests : TwoNodeAcceptanceTests, IDisposable
{
    private readonly List<IDisposable> owners = [];

    protected override ClusterState CreateClusterState()
    {
        var clock = new FakeTimeProvider(TestNow);
        var dispatchAccess = new FixedAccessContextAccessor("tenant-distributed");
        var distributedAccess = new FixedAccessContextAccessor(PersistenceScope.DefaultValue);
        var connection = new InMemoryProviderFactory().Create($"groundwork-two-node:{Guid.NewGuid():N}");
        var provider = new ServiceCollection()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<IPersistenceAccessContextAccessor>(distributedAccess)
            .AddGroundworkStorageProviderConnection(connection)
            .AddGroundworkDistributedRuntimeStores()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        owners.Add(provider);

        var checkpointState = new InMemoryRuntimeCheckpointStoreState();
        var workflowExecutionStore = new InMemoryWorkflowExecutionStateStore();
        var livenessStore = new InMemoryExecutionLivenessStateStore();
        var executableStore = new InMemoryWorkflowExecutableStore();
        var sourceReferenceStore = new InMemoryWorkflowExecutableSourceReferenceStore();

        DispatchPersistence OpenPersistence()
        {
            var dispatchStore = new InMemoryWorkflowDispatchStore(checkpointState);
            var checkpointStore = new InMemoryRuntimeCheckpointCommitStore(
                workflowExecutionStateStore: workflowExecutionStore,
                operationalStateStore: livenessStore,
                rootWriteLeaseManager: PassThroughRootWriteLeaseManager.Instance,
                state: checkpointState,
                timeProvider: clock,
                workflowDispatchStore: dispatchStore);
            return new DispatchPersistence(
                checkpointStore,
                checkpointStore,
                dispatchStore,
                workflowExecutionStore,
                executableStore,
                sourceReferenceStore,
                dispatchAccess);
        }

        using var scope = provider.CreateScope();
        return new ClusterState(
            scope.ServiceProvider.GetRequiredService<IExecutionPlacementStore>(),
            scope.ServiceProvider.GetRequiredService<IExecutionCommandTransport>(),
            livenessStore,
            clock,
            OpenPersistence);
    }

    [Fact(Skip = "E3 pending: checkpoint/outbox/dispatch/execution state must use provider-owned public Groundwork v2 storage.")]
    public override Task DispatchWorkflowChildStart_CommittedOnOneNode_ConvergesAfterBothNodesRestart() =>
        Task.CompletedTask;

    public void Dispose()
    {
        foreach (var owner in owners)
            owner.Dispose();
    }
}
