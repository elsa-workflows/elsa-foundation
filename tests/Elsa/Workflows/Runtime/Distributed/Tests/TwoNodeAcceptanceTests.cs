using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Distributed.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Tests;

/// <summary>
/// The W20 acceptance suite: two in-process "nodes" over one shared cluster state (placement store, command transport,
/// W5 liveness/ownership store, and a single <see cref="MutableTimeProvider"/> clock). These are the roadmap acceptance
/// tests — commands for one execution are routed and serialized correctly across nodes, and a node killed mid-drain
/// recovers on the survivor WITHOUT double-execution. Determinism comes entirely from advancing the shared fake clock
/// and driving pumps by hand; there are no sleeps.
/// </summary>
public sealed class TwoNodeAcceptanceTests
{
    private const string ExecutionId = "wf-acceptance-1";
    private const string NodeA = "node-a";
    private const string NodeB = "node-b";

    // Placement/transport visibility lease. Ownership (fencing) leases use their own duration below; the fencing token
    // itself is monotonic and never forgotten, which is the whole point of the kill test.
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FenceDuration = TimeSpan.FromSeconds(30);
    private readonly DateTimeOffset _now = new(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);

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

    private Cluster NewCluster()
    {
        var placementStore = new InMemoryExecutionPlacementStore();
        var transport = new InMemoryExecutionCommandTransport();
        var livenessStore = new InMemoryExecutionLivenessStateStore();
        var clock = new MutableTimeProvider(_now);

        var ownershipA = new RuntimeExecutionOwnershipService(livenessStore, clock, new RuntimeExecutionOwnershipOptions { OwnerId = NodeA, LeaseDuration = FenceDuration });
        var ownershipB = new RuntimeExecutionOwnershipService(livenessStore, clock, new RuntimeExecutionOwnershipOptions { OwnerId = NodeB, LeaseDuration = FenceDuration });

        var executorA = new FencingCommandExecutor(ownershipA);
        var executorB = new FencingCommandExecutor(ownershipB);

        var nodeA = new NodeHarness(NodeA, placementStore, transport, clock, executorA, LeaseDuration);
        var nodeB = new NodeHarness(NodeB, placementStore, transport, clock, executorB, LeaseDuration);

        return new Cluster(transport, clock, ownershipA, executorA, executorB, nodeA, nodeB);
    }

    private sealed record Cluster(
        InMemoryExecutionCommandTransport Transport,
        MutableTimeProvider Clock,
        IRuntimeExecutionOwnershipService OwnershipA,
        FencingCommandExecutor ExecutorA,
        FencingCommandExecutor ExecutorB,
        NodeHarness NodeA,
        NodeHarness NodeB);
}
