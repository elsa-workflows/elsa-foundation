using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Services;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Persistence.Core;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Services;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Tests;

/// <summary>
/// The W20 acceptance suite: two in-process "nodes" over one shared cluster state (placement store, command transport,
/// W5 liveness/ownership store, and a single <see cref="FakeTimeProvider"/> clock). These are the roadmap acceptance
/// tests — commands for one execution are routed and serialized correctly across nodes, and a node killed mid-drain
/// recovers on the survivor WITHOUT double-execution. Determinism comes entirely from advancing the shared fake clock
/// and driving pumps by hand; there are no sleeps.
/// </summary>
/// <remarks>
/// The suite is abstract over the shared placement store and command transport so the SAME scenarios prove both the
/// in-memory harness stores (W20) and the durable Groundwork-backed stores (W27) — see the concrete fixtures in
/// <c>InMemoryTwoNodeAcceptanceTests</c> and <c>GroundworkTwoNodeAcceptanceTests</c>.
/// </remarks>
public abstract class TwoNodeAcceptanceTests
{
    private const string ExecutionId = "wf-acceptance-1";
    private const string NodeA = "node-a";
    private const string NodeB = "node-b";

    // Placement/transport visibility lease. Ownership (fencing) leases use their own duration below; the fencing token
    // itself is monotonic and never forgotten, which is the whole point of the kill test.
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FenceDuration = TimeSpan.FromSeconds(30);
    protected static DateTimeOffset TestNow { get; } = new(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);
    private readonly DateTimeOffset _now = TestNow;

    [Fact]
    public async Task Routing_CommandForRemoteExecution_ForwardsAndDrainsOnOwningNodeInOrder()
    {
        var cluster = NewCluster();

        // Node A owns the execution.
        await cluster.NodeA.PlacementService.TryClaimAsync(ExecutionId);

        // Two commands for that execution arrive as inbound work on node B, which does not own it.
        var first = await cluster.NodeB.DispatchAsync(ExecutionId, NodeHarness.Envelope(ExecutionId, "env-1", _now));
        var second = await cluster.NodeB.DispatchAsync(ExecutionId, NodeHarness.Envelope(ExecutionId, "env-2", _now));

        // Both are accepted for routing (forwarded to the owning node) rather than run locally on B.
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Deferred, first.Status);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Deferred, second.Status);
        Assert.Empty(cluster.ExecutorB.Committed);

        // The owning node's pump drains the forwarded commands in enqueue order, exactly once each.
        var sweep = await cluster.NodeA.Pump.SweepAsync();

        Assert.Equal(2, sweep.DispatchedCommandCount);
        Assert.Equal(2, sweep.AckedCount);
        Assert.Equal(new[] { "env-1", "env-2" }, cluster.ExecutorA.Committed);
        Assert.Empty(cluster.ExecutorB.Committed);
        Assert.Equal(0, await cluster.Transport.CountPendingAsync(ExecutionId));
    }

    [Fact]
    public async Task NodeKilledMidDrain_SurvivorReDrivesInbox_AndDeadNodesLateCommitIsFenced()
    {
        var cluster = NewCluster();

        // Node A takes placement and begins a drain: it acquires the W5 fencing lease (token 1) for the execution.
        await cluster.NodeA.PlacementService.TryClaimAsync(ExecutionId);
        var deadNodeLease = await cluster.OwnershipA.AcquireAsync(ExecutionId);

        // A command for the execution is in flight on A: enqueued to the durable inbox and leased by A, but A dies
        // before acking it (the classic kill-mid-drain: work claimed, not yet completed).
        await cluster.Transport.SendAsync(ExecutionId, NodeHarness.Envelope(ExecutionId, "env-1", _now), _now);
        var leasedByA = await cluster.Transport.LeaseAsync(ExecutionId, NodeA, _now, LeaseDuration, maxItems: 10);
        Assert.Single(leasedByA);

        // A is gone. Time advances past both the placement lease and the transport visibility lease so the survivor can
        // claim ownership and see the stranded command again. The fencing token counter is NOT time-based: it persists.
        cluster.Clock.Advance(LeaseDuration + TimeSpan.FromSeconds(1));

        // Failover: node B's pump claims placement, re-leases the stranded command, drains it locally under a fresh,
        // strictly greater fencing token (token 2), and acks. This asserts inbox re-drive on failover, not merely that
        // a stale write would be rejected.
        var sweep = await cluster.NodeB.Pump.SweepAsync();

        Assert.Equal(1, sweep.ClaimedCount);
        Assert.Equal(1, sweep.DispatchedCommandCount);
        Assert.Equal(1, sweep.AckedCount);
        Assert.Equal(new[] { "env-1" }, cluster.ExecutorB.Committed);
        Assert.Equal(0, await cluster.Transport.CountPendingAsync(ExecutionId));
        var owner = await cluster.NodeB.PlacementService.FindOwnerAsync(ExecutionId);
        Assert.Equal(NodeB, owner!.OwnerId);

        // The heart of the unit: the dead node "wakes up" and tries to commit the checkpoint it was draining, presenting
        // its now-stale fencing token. The single-writer commit funnel rejects it, so A's interrupted turn cannot
        // produce a second durable execution of the same command.
        var fenced = await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(async () =>
            await cluster.OwnershipA.EnsureCurrentAsync(ExecutionId, deadNodeLease.FencingToken));

        Assert.Equal(deadNodeLease.FencingToken, fenced.PresentedFencingToken);
        Assert.True(fenced.CurrentFencingToken > deadNodeLease.FencingToken);

        // Net effect across the cluster: the command committed exactly once (on the survivor), never on the dead node.
        Assert.Empty(cluster.ExecutorA.Committed);
        Assert.Single(cluster.ExecutorB.Committed);
    }

    [Fact]
    public async Task DispatchWorkflowChildStart_CommittedOnOneNode_ConvergesAfterBothNodesRestart()
    {
        var clusterState = CreateClusterState();
        CommittedDispatch dispatch;
        using (var commitNode = clusterState.OpenDispatchPersistence())
            dispatch = await CommitDispatchAsync(commitNode);

        RuntimeExecutionFence? deadNodeFence = null;
        async ValueTask CommitEffectAsync(
            WorkflowExecutionCommandEnvelope envelope,
            RuntimeExecutionLease lease,
            CancellationToken cancellationToken)
        {
            if (StringComparer.Ordinal.Equals(lease.OwnerId, NodeA))
                deadNodeFence = lease.ToFence();
            await CommitChildInFreshPersistenceAsync(
                clusterState,
                dispatch,
                envelope,
                lease.ToFence(),
                commitSuffix: "current",
                cancellationToken: cancellationToken);
        }

        var cluster = NewCluster(clusterState, CommitEffectAsync);

        // Node A owns the child placement. Node B is recreated after node A committed the checkpoint/outbox, proving
        // the handler does not depend on process-local intent state.
        await cluster.NodeA.PlacementService.TryClaimAsync(dispatch.Record.ChildWorkflowExecutionId);
        using var restartedHandlerState = clusterState.OpenDispatchPersistence();
        var outbox = Assert.Single(await restartedHandlerState.OutboxStore.GetDeliverableAsync(
            new RuntimePostCommitOutboxQuery(_now.AddMinutes(1), 10, dispatch.Record.ParentWorkflowExecutionId)));
        var handler = NewChildStartHandler(restartedHandlerState, cluster.RestartNodeB().Provider);

        await handler.HandleAsync(outbox.Intent);
        await handler.HandleAsync(outbox.Intent);
        await restartedHandlerState.OutboxStore.RecordDeliveryResultAsync(
            new RuntimePostCommitOutboxDeliveryResult(
                outbox.OutboxItemId,
                RuntimePostCommitOutboxStatus.Delivered,
                _now.AddSeconds(2)));
        Assert.Empty(await restartedHandlerState.OutboxStore.GetDeliverableAsync(
            new RuntimePostCommitOutboxQuery(_now.AddMinutes(1), 10, dispatch.Record.ParentWorkflowExecutionId)));

        // The only activity-facing seam remains WorkflowStartDispatcher -> IWorkflowExecutionActorProvider. Duplicate
        // at-least-once delivery produces duplicate transport evidence, while the lifecycle record is admitted once.
        Assert.Equal(2, await cluster.Transport.CountPendingAsync(dispatch.Record.ChildWorkflowExecutionId));
        Assert.Equal(WorkflowDispatchStatus.Started, (await restartedHandlerState.DispatchStore.FindAsync(dispatch.Record.DispatchId))!.Status);

        // The eligible node processes one leased item but dies before acknowledging it. After the visibility timeout a
        // brand-new actor provider re-drives both the stranded item and its duplicate. The child checkpoint commit ID is
        // deterministic, so provider-backed replay converges even though the mailbox cache was lost in the restart.
        var leased = Assert.Single(await cluster.Transport.LeaseAsync(
            dispatch.Record.ChildWorkflowExecutionId,
            NodeA,
            _now,
            LeaseDuration,
            maxItems: 1));
        var firstOwner = cluster.RestartNodeA();
        var firstDispatch = await firstOwner.DispatchAsync(dispatch.Record.ChildWorkflowExecutionId, leased.Envelope);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, firstDispatch.Status);
        cluster.Clock.Advance(LeaseDuration + TimeSpan.FromSeconds(1));
        var restartedOwner = cluster.RestartNodeB();

        var sweep = await restartedOwner.Pump.SweepAsync();

        // Both transport items are acknowledged, but the durable execution and authenticated inspection contracts show
        // one logical child with the original tenant, authority, partition, run-kind, and nesting-depth provenance.
        Assert.Equal(2, sweep.DispatchedCommandCount);
        Assert.Equal(2, sweep.AckedCount);
        Assert.Equal(0, await cluster.Transport.CountPendingAsync(dispatch.Record.ChildWorkflowExecutionId));
        Assert.Equal(
            NodeB,
            (await restartedOwner.PlacementService.FindOwnerAsync(dispatch.Record.ChildWorkflowExecutionId))!.OwnerId);
        var staleFence = Assert.IsType<RuntimeExecutionFence>(deadNodeFence);
        using (var staleNode = clusterState.OpenDispatchPersistence())
        {
            await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(() =>
                CommitChildExecutionAsync(
                    staleNode,
                    dispatch,
                    leased.Envelope,
                    staleFence,
                    commitSuffix: "stale-late-writer",
                    cancellationToken: CancellationToken.None).AsTask());
        }

        using var inspectionNode = clusterState.OpenDispatchPersistence();
        var child = Assert.IsType<WorkflowExecutionState>(
            await inspectionNode.WorkflowExecutionStore.FindAsync(dispatch.Record.ChildWorkflowExecutionId));
        Assert.Equal(WorkflowExecutionStatus.Running, child.Status);
        Assert.Equal(dispatch.Record.ChildExecutable, child.PinnedExecutable);
        Assert.Equal(dispatch.Record.ParentWorkflowExecutionId, child.ParentWorkflowExecutionId);
        Assert.Equal(dispatch.Record.TenantId, child.TenantId);
        Assert.Equal(dispatch.Record.Partition, child.Partition);
        Assert.Equal(dispatch.Record.RunKind, child.RunKind);
        AssertAuthority(dispatch.Record.Authority, child.Authority);
        Assert.Equal(2, child.DispatchNestingDepth);

        var parent = Assert.IsType<WorkflowExecutionState>(
            await inspectionNode.WorkflowExecutionStore.FindAsync(dispatch.Record.ParentWorkflowExecutionId));
        Assert.Equal(WorkflowExecutionStatus.Running, parent.Status);
        Assert.Equal(dispatch.ParentExecutable.Identity, parent.PinnedExecutable);
        Assert.Equal(dispatch.Record.TenantId, parent.TenantId);
        Assert.Equal(dispatch.Record.Partition, parent.Partition);
        Assert.Equal(dispatch.Record.RunKind, parent.RunKind);
        AssertAuthority(dispatch.Record.Authority, parent.Authority);
        Assert.Equal(1, parent.DispatchNestingDepth);

        var inspected = await new GetWorkflowDispatchRequestHandler(
                inspectionNode.DispatchStore,
                inspectionNode.AccessContextAccessor)
            .Handle(new GetWorkflowDispatch(dispatch.Record.DispatchId), CancellationToken.None);
        Assert.NotNull(inspected.Dispatch);
        Assert.Equal(WorkflowDispatchStatus.Started, inspected.Dispatch.Status);
        Assert.Equal(dispatch.Record.ChildWorkflowExecutionId, inspected.Dispatch.ChildWorkflowExecutionId);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new GetWorkflowDispatchRequestHandler(
                    inspectionNode.DispatchStore,
                    new FixedAccessContextAccessor("another-tenant"))
                .Handle(new GetWorkflowDispatch(dispatch.Record.DispatchId), CancellationToken.None));
    }

    /// <summary>Creates the cluster-shared routing and dispatch persistence used by both nodes.</summary>
    protected abstract ClusterState CreateClusterState();

    private Cluster NewCluster(
        ClusterState? clusterState = null,
        Func<WorkflowExecutionCommandEnvelope, RuntimeExecutionLease, CancellationToken, ValueTask>? commitEffect = null)
    {
        clusterState ??= CreateClusterState();
        var placementStore = clusterState.PlacementStore;
        var transport = clusterState.Transport;
        var livenessStore = clusterState.LivenessStore;
        var clock = clusterState.Clock;

        var ownershipA = new RuntimeExecutionOwnershipService(livenessStore, clock, new RuntimeExecutionOwnershipOptions { OwnerId = NodeA, LeaseDuration = FenceDuration });
        var ownershipB = new RuntimeExecutionOwnershipService(livenessStore, clock, new RuntimeExecutionOwnershipOptions { OwnerId = NodeB, LeaseDuration = FenceDuration });

        var executorA = new FencingCommandExecutor(ownershipA, commitEffect);
        var executorB = new FencingCommandExecutor(ownershipB, commitEffect);

        var nodeA = new NodeHarness(NodeA, placementStore, transport, clock, executorA, LeaseDuration);
        var nodeB = new NodeHarness(NodeB, placementStore, transport, clock, executorB, LeaseDuration);

        return new Cluster(
            placementStore,
            transport,
            livenessStore,
            clock,
            ownershipA,
            ownershipB,
            executorA,
            executorB,
            nodeA,
            nodeB,
            commitEffect);
    }

    private sealed record Cluster(
        IExecutionPlacementStore PlacementStore,
        IExecutionCommandTransport Transport,
        IExecutionLivenessStateStore LivenessStore,
        FakeTimeProvider Clock,
        IRuntimeExecutionOwnershipService OwnershipA,
        IRuntimeExecutionOwnershipService OwnershipB,
        FencingCommandExecutor ExecutorA,
        FencingCommandExecutor ExecutorB,
        NodeHarness NodeA,
        NodeHarness NodeB,
        Func<WorkflowExecutionCommandEnvelope, RuntimeExecutionLease, CancellationToken, ValueTask>? CommitEffect)
    {
        public NodeHarness RestartNodeA() => Restart(TwoNodeAcceptanceTests.NodeA);
        public NodeHarness RestartNodeB() => Restart(TwoNodeAcceptanceTests.NodeB);

        private NodeHarness Restart(string nodeId)
        {
            var ownership = new RuntimeExecutionOwnershipService(
                LivenessStore,
                Clock,
                new RuntimeExecutionOwnershipOptions { OwnerId = nodeId, LeaseDuration = FenceDuration });
            var executor = new FencingCommandExecutor(ownership, CommitEffect);
            return new NodeHarness(nodeId, PlacementStore, Transport, Clock, executor, LeaseDuration);
        }
    }

    private sealed record CommittedDispatch(
        WorkflowDispatchRecord Record,
        WorkflowExecutable ParentExecutable,
        WorkflowExecutable ChildExecutable);

    private async Task<CommittedDispatch> CommitDispatchAsync(DispatchPersistence persistence)
    {
        var identity = new WorkflowDispatchIdentity("parent-distributed", "activity-distributed");
        const string parentWorkflowExecutionId = "parent-distributed";
        const string parentActivityExecutionId = "activity-distributed";
        var childExecutable = NewChildExecutable();
        var parentExecutable = NewParentExecutable(childExecutable.Identity);
        var sourceReference = NewChildSourceReference(childExecutable.Identity);
        var source = WorkflowExecutableSourceProvenance.From(sourceReference);
        await persistence.ExecutableStore.SaveAsync(parentExecutable);
        await persistence.ExecutableStore.SaveAsync(childExecutable);
        await persistence.SourceReferenceStore.SaveAsync(sourceReference);
        var dispatch = new WorkflowDispatchRecord(
            identity.DispatchId,
            parentWorkflowExecutionId,
            parentActivityExecutionId,
            identity.ChildWorkflowExecutionId,
            childExecutable.Identity,
            source,
            WorkflowDispatchMode.FireAndForget,
            WorkflowDispatchStatus.Pending,
            "correlation-distributed",
            "tenant-distributed",
            new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue),
            WorkflowRunKind.PublishedRun,
            new WorkflowExecutionAuthoritySnapshot("system-dispatch", "root-dispatch"),
            [new WorkflowDispatchInputDescriptor("message", "System.String")],
            _now,
            _now,
            metadata: null,
            dispatchNestingDepth: 2,
            testScope: null);

        var payload = new WorkflowDispatchStartPayload(
            dispatch.DispatchId,
            dispatch.ParentWorkflowExecutionId,
            dispatch.ParentActivityExecutionId,
            dispatch.ChildWorkflowExecutionId,
            dispatch.ChildExecutable,
            childSource: null,
            new Dictionary<string, JsonElement>
            {
                ["message"] = JsonSerializer.SerializeToElement("hello from distributed dispatch")
            },
            dispatch.CorrelationId,
            dispatch.TenantId,
            dispatch.Partition,
            dispatch.RunKind,
            dispatch.Authority,
            parentExecutable: parentExecutable.Identity,
            dispatchNodeId: "dispatch-node",
            dispatchNestingDepth: 2,
            testScope: null);
        var intent = new RuntimePostCommitIntent(
            identity.StartIntentId,
            dispatch.ParentWorkflowExecutionId,
            DispatchWorkflowConstants.StartChildIntentKind,
            _now,
            dispatch.ParentActivityExecutionId,
            identity.StartIdempotencyKey,
            JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var parentState = new WorkflowExecutionState(
            parentWorkflowExecutionId,
            parentExecutable.Identity,
            WorkflowExecutionStatus.Running,
            null,
            _now.AddMinutes(-1),
            _now.AddMinutes(-1),
            _now,
            null,
            dispatch.CorrelationId,
            null,
            dispatch.TenantId,
            new Dictionary<string, string>())
        {
            Partition = dispatch.Partition,
            RunKind = dispatch.RunKind,
            Authority = dispatch.Authority,
            DispatchNestingDepth = 1
        };

        var commit = new RuntimeCheckpointCommit(
            "commit-distributed-dispatch",
            new RuntimeCheckpoint(
                "checkpoint-distributed-dispatch",
                "DispatchWorkflow",
                parentWorkflowExecutionId,
                _now,
                [parentActivityExecutionId],
                new Dictionary<string, string>()),
            new RuntimeCheckpointStateChangeSet(
                workflowExecution: new RuntimeStateChange<WorkflowExecutionState>(
                    parentState.WorkflowExecutionId,
                    RuntimeStateChangeOperation.Upsert,
                    parentState,
                    new Dictionary<string, string>()),
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: [],
                workflowDispatches:
                [
                    new RuntimeStateChange<WorkflowDispatchRecord>(
                        dispatch.DispatchId,
                        RuntimeStateChangeOperation.Upsert,
                        dispatch,
                        new Dictionary<string, string>())
                ]),
            [intent],
            new Dictionary<string, string>());
        var committer = NewCommitter(persistence.CheckpointStore);
        var result = await committer.CommitAsync(commit);
        Assert.True(result.Succeeded);
        Assert.Single(result.PendingPostCommitWorkIds);
        Assert.NotNull(await persistence.DispatchStore.FindAsync(dispatch.DispatchId));

        return new CommittedDispatch(dispatch, parentExecutable, childExecutable);
    }

    private ChildStartExecutor NewChildStartHandler(
        DispatchPersistence persistence,
        IWorkflowExecutionActorProvider provider)
    {
        var startDispatcher = new WorkflowStartDispatcher(
            persistence.ExecutableStore,
            persistence.SourceReferenceStore,
            provider,
            new FixedRuntimeExecutionIdGenerator("unused-retained-id"),
            new FixedTimeProvider(_now),
            partitionAccessor: null,
            startPolicy: new AllowWorkflowExecutableStartPolicy(),
            workflowDispatchStore: persistence.DispatchStore,
            workflowExecutionStateStore: persistence.WorkflowExecutionStore);

        return new ChildStartExecutor(startDispatcher, persistence.DispatchStore, new FixedTimeProvider(_now.AddSeconds(1)));
    }

    private async ValueTask CommitChildInFreshPersistenceAsync(
        ClusterState clusterState,
        CommittedDispatch dispatch,
        WorkflowExecutionCommandEnvelope envelope,
        RuntimeExecutionFence expectedFence,
        string commitSuffix,
        CancellationToken cancellationToken)
    {
        using var persistence = clusterState.OpenDispatchPersistence();
        await CommitChildExecutionAsync(
            persistence,
            dispatch,
            envelope,
            expectedFence,
            commitSuffix,
            cancellationToken);
    }

    private async ValueTask CommitChildExecutionAsync(
        DispatchPersistence persistence,
        CommittedDispatch dispatch,
        WorkflowExecutionCommandEnvelope envelope,
        RuntimeExecutionFence expectedFence,
        string commitSuffix,
        CancellationToken cancellationToken)
    {
        var payload = envelope.Command.Payload?.Deserialize<WorkflowExecutionStartCommandPayload>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(payload);
        Assert.Equal(dispatch.ChildExecutable.Identity, payload.PinnedExecutable);
        Assert.Equal(dispatch.Record.ParentWorkflowExecutionId, payload.ParentWorkflowExecutionId);
        Assert.Equal(dispatch.Record.TenantId, payload.TenantId);
        Assert.Equal(dispatch.Record.Partition, payload.Partition);
        Assert.Equal(dispatch.Record.RunKind, payload.RunKind);
        AssertAuthority(dispatch.Record.Authority, payload.Authority);
        Assert.Equal(2, payload.DispatchNestingDepth);
        Assert.Null(payload.PinnedSource);
        var retainedAuthority = Assert.IsType<WorkflowExecutableRetainedDependencyAuthority>(
            payload.StartAuthority?.RetainedDependency);
        Assert.Equal(dispatch.ParentExecutable.Identity.ArtifactId, retainedAuthority.ParentArtifactId);
        Assert.Equal(dispatch.ParentExecutable.Identity.ArtifactHash, retainedAuthority.ParentArtifactHash);
        Assert.Equal("dispatch-node", retainedAuthority.DispatchNodeId);

        var state = new WorkflowExecutionState(
            envelope.WorkflowExecutionId,
            dispatch.ChildExecutable.Identity,
            WorkflowExecutionStatus.Running,
            null,
            _now,
            _now,
            _now,
            null,
            dispatch.Record.CorrelationId,
            dispatch.Record.ParentWorkflowExecutionId,
            dispatch.Record.TenantId,
            new Dictionary<string, string>())
        {
            Partition = dispatch.Record.Partition,
            RunKind = dispatch.Record.RunKind,
            Authority = dispatch.Record.Authority,
            DispatchNestingDepth = payload.DispatchNestingDepth
        };
        var commit = new RuntimeCheckpointCommit(
            $"checkpoint-commit:{dispatch.Record.DispatchId}:child-start:{commitSuffix}",
            new RuntimeCheckpoint(
                $"checkpoint:{dispatch.Record.DispatchId}:child-start:{commitSuffix}",
                "WorkflowStarted",
                envelope.WorkflowExecutionId,
                _now,
                [],
                new Dictionary<string, string>()),
            new RuntimeCheckpointStateChangeSet(
                new RuntimeStateChange<WorkflowExecutionState>(
                    state.WorkflowExecutionId,
                    RuntimeStateChangeOperation.Upsert,
                    state,
                    new Dictionary<string, string>()),
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: []),
            [],
            new Dictionary<string, string>())
        {
            ExpectedFence = expectedFence
        };
        var result = await NewCommitter(persistence.CheckpointStore).CommitAsync(commit, cancellationToken);
        Assert.True(result.Succeeded);
    }

    private static RuntimeCheckpointCommitter NewCommitter(IRuntimeCheckpointCommitStore store) =>
        new(
            new ImmediateRuntimeCheckpointPersistencePolicy(),
            store,
            new AsyncLocalRuntimeExecutionOwnershipContextAccessor(),
            enrichers: [],
            intentHandlerContributions:
            [
                new RuntimePostCommitIntentHandlerContribution(
                    DispatchWorkflowConstants.StartChildIntentKind,
                    typeof(ChildStartExecutor))
            ]);

    private static void AssertAuthority(
        WorkflowExecutionAuthoritySnapshot expected,
        WorkflowExecutionAuthoritySnapshot? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.SystemIdentity, actual.SystemIdentity);
        Assert.Equal(expected.RootInitiator, actual.RootInitiator);
        Assert.Equal(expected.Metadata, actual.Metadata);
    }

    private static WorkflowExecutable NewChildExecutable()
    {
        var identity = new WorkflowExecutableIdentity(
            "artifact-distributed-child",
            "definition-distributed-child",
            "version-distributed-child",
            "1.0.0",
            "sha256:distributed-child");
        var root = new ExecutableNode(
            "node-root",
            "activity-root",
            "Elsa.TestActivities.Noop",
            "1.0.0",
            new RuntimeActivityDescriptor(
                "test.noop",
                RuntimeActivityDescriptor.InitialSchemaVersion,
                JsonSerializer.SerializeToElement(new { type = "noop" })),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string>());
        return new WorkflowExecutable(
            identity,
            root,
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            new DateTimeOffset(2026, 7, 20, 8, 30, 0, TimeSpan.Zero),
            new Dictionary<string, string>(),
            IncidentStrategyBuiltIns.FaultReference);
    }

    private static WorkflowExecutable NewParentExecutable(WorkflowExecutableIdentity childIdentity)
    {
        var identity = new WorkflowExecutableIdentity(
            "artifact-distributed-parent",
            "definition-distributed-parent",
            "version-distributed-parent",
            "1.0.0",
            "sha256:distributed-parent");
        var root = new ExecutableNode(
            "dispatch-node",
            "activity-dispatch",
            "Elsa.DispatchWorkflow",
            "1.0.0",
            new RuntimeActivityDescriptor(
                "elsa.dispatch-workflow",
                RuntimeActivityDescriptor.InitialSchemaVersion,
                JsonSerializer.SerializeToElement(new { type = "dispatch-workflow" })),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string>());
        return new WorkflowExecutable(
            identity,
            root,
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            new DateTimeOffset(2026, 7, 20, 8, 30, 0, TimeSpan.Zero),
            new Dictionary<string, string>(),
            inputContract: null,
            dependencies:
            [
                new WorkflowExecutableDependency(
                    childIdentity.ArtifactId,
                    childIdentity.ArtifactHash,
                    [root.ExecutableNodeId])
            ],
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
    }

    private static WorkflowExecutableSourceReference NewChildSourceReference(WorkflowExecutableIdentity identity) =>
        new(
            "source-distributed-child",
            identity.ArtifactId,
            "definition",
            identity.DefinitionId,
            identity.DefinitionVersionId,
            identity.DefinitionId,
            identity.DefinitionVersionId,
            identity.ArtifactVersion,
            new DateTimeOffset(2026, 7, 20, 8, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 20, 8, 45, 0, TimeSpan.Zero),
            WorkflowExecutableReferenceScope.Published,
            PublicationId: "publication-distributed",
            SlotId: "production");

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FixedRuntimeExecutionIdGenerator(string workflowExecutionId) : IRuntimeExecutionIdGenerator
    {
        private int _commandId;
        private int _envelopeId;
        private int _activityId;

        public string NewWorkflowExecutionId() => workflowExecutionId;
        public string NewWorkflowExecutionCommandId() => $"command-{Interlocked.Increment(ref _commandId)}";
        public string NewWorkflowExecutionCommandEnvelopeId() => $"envelope-{Interlocked.Increment(ref _envelopeId)}";
        public string NewActivityExecutionId() => $"activity-{Interlocked.Increment(ref _activityId)}";
    }

    protected sealed record ClusterState(
        IExecutionPlacementStore PlacementStore,
        IExecutionCommandTransport Transport,
        IExecutionLivenessStateStore LivenessStore,
        FakeTimeProvider Clock,
        Func<DispatchPersistence> OpenDispatchPersistence);

    protected sealed class DispatchPersistence(
        IRuntimeCheckpointCommitStore checkpointStore,
        IRuntimePostCommitOutboxStore outboxStore,
        IWorkflowDispatchStore dispatchStore,
        IWorkflowExecutionStateStore workflowExecutionStore,
        IWorkflowExecutableStore executableStore,
        IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
        IPersistenceAccessContextAccessor accessContextAccessor,
        IDisposable? owner = null) : IDisposable
    {
        public IRuntimeCheckpointCommitStore CheckpointStore { get; } = checkpointStore;
        public IRuntimePostCommitOutboxStore OutboxStore { get; } = outboxStore;
        public IWorkflowDispatchStore DispatchStore { get; } = dispatchStore;
        public IWorkflowExecutionStateStore WorkflowExecutionStore { get; } = workflowExecutionStore;
        public IWorkflowExecutableStore ExecutableStore { get; } = executableStore;
        public IWorkflowExecutableSourceReferenceStore SourceReferenceStore { get; } = sourceReferenceStore;
        public IPersistenceAccessContextAccessor AccessContextAccessor { get; } = accessContextAccessor;

        public void Dispose() => owner?.Dispose();
    }

    protected sealed class FixedAccessContextAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } =
            PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }

    protected sealed class PassThroughRootWriteLeaseManager : IWorkflowExecutableRootWriteLeaseManager
    {
        public static PassThroughRootWriteLeaseManager Instance { get; } = new();

        public ValueTask ExecuteAsync(
            string artifactId,
            string leaseId,
            Func<CancellationToken, ValueTask> write,
            CancellationToken cancellationToken = default) =>
            write(cancellationToken);
    }
}

/// <summary>The W20 acceptance suite over the in-memory harness stores (the original single-process cluster state).</summary>
public sealed class InMemoryTwoNodeAcceptanceTests : TwoNodeAcceptanceTests
{
    protected override ClusterState CreateClusterState()
    {
        var clock = new FakeTimeProvider(TestNow);
        var checkpointState = new InMemoryRuntimeCheckpointStoreState();
        var workflowExecutionStore = new InMemoryWorkflowExecutionStateStore();
        var livenessStore = new InMemoryExecutionLivenessStateStore();
        var executableStore = new InMemoryWorkflowExecutableStore();
        var sourceReferenceStore = new InMemoryWorkflowExecutableSourceReferenceStore();
        var accessContext = new FixedAccessContextAccessor("tenant-distributed");

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
                accessContext);
        }

        return new ClusterState(
            new InMemoryExecutionPlacementStore(),
            new InMemoryExecutionCommandTransport(),
            livenessStore,
            clock,
            OpenPersistence);
    }
}
