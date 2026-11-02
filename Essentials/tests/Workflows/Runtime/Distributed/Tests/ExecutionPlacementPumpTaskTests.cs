using System.Collections.Concurrent;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Elsa.Workflows.Runtime.Distributed.Services;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Tests;

public sealed class ExecutionPlacementPumpTaskTests
{
    private const string ExecutionId = "wf-1";
    private const string NodeA = "node-a";
    private const string NodeB = "node-b";
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);
    private readonly DateTimeOffset _now = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sweep_ClaimsUnownedBacklog_DispatchesLocally_AndAcks()
    {
        var store = new InMemoryExecutionPlacementStore();
        var transport = new InMemoryExecutionCommandTransport();
        var clock = new FakeTimeProvider(_now);
        var executor = new RecordingCommandExecutor();
        var node = new NodeHarness(NodeA, store, transport, clock, executor, LeaseDuration);

        // A command lands in the transport inbox for an execution nobody owns yet.
        await transport.SendAsync(ExecutionId, NodeHarness.Envelope(ExecutionId, "env-1", _now), _now);

        var result = await node.Pump.SweepOnceAsync();

        Assert.Equal(1, result.ClaimedCount);
        Assert.Equal(1, result.DispatchedCommandCount);
        Assert.Equal(1, result.AckedCount);
        Assert.Equal(new[] { "env-1" }, executor.Processed);
        Assert.Equal(0, await transport.CountPendingAsync(ExecutionId));
    }

    [Fact]
    public async Task Sweep_SkipsExecutionOwnedByAnotherLiveNode()
    {
        var store = new InMemoryExecutionPlacementStore();
        var transport = new InMemoryExecutionCommandTransport();
        var clock = new FakeTimeProvider(_now);

        var nodeA = new NodeHarness(NodeA, store, transport, clock, new RecordingCommandExecutor(), LeaseDuration);
        var executorB = new RecordingCommandExecutor();
        var nodeB = new NodeHarness(NodeB, store, transport, clock, executorB, LeaseDuration);

        // Node A owns the execution; a command arrives in the shared inbox.
        await nodeA.PlacementService.TryClaimAsync(ExecutionId);
        await transport.SendAsync(ExecutionId, NodeHarness.Envelope(ExecutionId, "env-1", _now), _now);

        // Node B's sweep must not steal a live-owned execution.
        var result = await nodeB.Pump.SweepOnceAsync();

        Assert.Equal(0, result.ClaimedCount);
        Assert.Empty(executorB.Processed);
        Assert.Equal(1, await transport.CountPendingAsync(ExecutionId));
    }

    [Fact]
    public async Task Sweep_RenewsPlacementThisNodeHolds()
    {
        var store = new InMemoryExecutionPlacementStore();
        var transport = new InMemoryExecutionCommandTransport();
        var clock = new FakeTimeProvider(_now);
        var node = new NodeHarness(NodeA, store, transport, clock, new RecordingCommandExecutor(), LeaseDuration);

        var initial = await node.PlacementService.TryClaimAsync(ExecutionId);

        // Advance within the lease window and sweep; the held lease should be renewed (expiry pushed out, token bumped).
        clock.Advance(TimeSpan.FromSeconds(10));
        var result = await node.Pump.SweepOnceAsync();

        Assert.Equal(1, result.RenewedCount);
        var renewed = await node.PlacementService.FindOwnerAsync(ExecutionId);
        Assert.NotNull(renewed);
        Assert.True(renewed!.PlacementToken > initial.Lease.PlacementToken);
        Assert.Equal(_now.AddSeconds(10) + LeaseDuration, renewed.ExpiresAt);
    }

    [Fact]
    public async Task Sweep_AfterOwnerPlacementExpires_ClaimsAndReDrivesOnFailover()
    {
        var store = new InMemoryExecutionPlacementStore();
        var transport = new InMemoryExecutionCommandTransport();
        var clock = new FakeTimeProvider(_now);

        var executorA = new RecordingCommandExecutor();
        var nodeA = new NodeHarness(NodeA, store, transport, clock, executorA, LeaseDuration);
        var executorB = new RecordingCommandExecutor();
        var nodeB = new NodeHarness(NodeB, store, transport, clock, executorB, LeaseDuration);

        // Node A owns the execution and has leased the command (in-flight) but dies before acking.
        await nodeA.PlacementService.TryClaimAsync(ExecutionId);
        await transport.SendAsync(ExecutionId, NodeHarness.Envelope(ExecutionId, "env-1", _now), _now);
        var leasedByA = await transport.LeaseAsync(ExecutionId, NodeA, _now, LeaseDuration, maxItems: 10);
        Assert.Single(leasedByA);

        // Time passes beyond both the placement lease and the transport visibility lease: A is gone.
        clock.Advance(LeaseDuration + TimeSpan.FromSeconds(1));

        var result = await nodeB.Pump.SweepOnceAsync();

        // Node B claims placement, re-leases the stranded command, dispatches it locally, and acks.
        Assert.Equal(1, result.ClaimedCount);
        Assert.Equal(1, result.DispatchedCommandCount);
        Assert.Equal(1, result.AckedCount);
        Assert.Equal(new[] { "env-1" }, executorB.Processed);
        Assert.Empty(executorA.Processed);
        Assert.Equal(0, await transport.CountPendingAsync(ExecutionId));

        var owner = await nodeB.PlacementService.FindOwnerAsync(ExecutionId);
        Assert.Equal(NodeB, owner!.OwnerId);
    }

    [Fact]
    public async Task Sweep_DoesNotAckWhenDispatchIsDeferred()
    {
        var store = new InMemoryExecutionPlacementStore();
        var transport = new InMemoryExecutionCommandTransport();
        var clock = new FakeTimeProvider(_now);

        // Node A owns placement so node B's local dispatch would forward (Deferred) instead of draining.
        var nodeA = new NodeHarness(NodeA, store, transport, clock, new RecordingCommandExecutor(), LeaseDuration);
        await nodeA.PlacementService.TryClaimAsync(ExecutionId);

        // A command is in the inbox. Node B cannot own it (A is live), so its sweep claims nothing and acks nothing.
        await transport.SendAsync(ExecutionId, NodeHarness.Envelope(ExecutionId, "env-1", _now), _now);
        var nodeB = new NodeHarness(NodeB, store, transport, clock, new RecordingCommandExecutor(), LeaseDuration);

        var result = await nodeB.Pump.SweepOnceAsync();

        Assert.Equal(0, result.AckedCount);
        Assert.Equal(1, await transport.CountPendingAsync(ExecutionId));
    }

    /// <summary>
    /// A command whose drain keeps running must not hold the sweep (#2831). The sweep stops waiting after
    /// <c>InlineDispatchWait</c>. Its next sweep renews the node's placement and delivers a Cancel to the busy mailbox
    /// at once, while a further non-Cancel command waits until the drain ends. A command whose transport lease lapses
    /// mid-drain is not queued again, and every command is acknowledged.
    /// </summary>
    [Fact]
    public async Task A_running_drain_does_not_hold_the_sweep_from_renewing_or_delivering_a_cancel()
    {
        var store = new InMemoryExecutionPlacementStore();
        var transport = new InMemoryExecutionCommandTransport();
        var clock = new FakeTimeProvider(_now);
        var executor = new HoldingCommandExecutor("env-1");
        var node = new NodeHarness(NodeA, store, transport, clock, executor, LeaseDuration);
        await transport.SendAsync(ExecutionId, NodeHarness.Envelope(ExecutionId, "env-1", _now), _now);

        // The first sweep claims the execution and starts env-1's drain, which keeps running.
        var first = await SweepAdvancingAsync(node, clock);
        Assert.Equal((1, 1, 0), (first.ClaimedCount, first.DispatchedCommandCount, first.AckedCount));
        Assert.True(executor.Holding.Task.IsCompleted);

        // Within the placement lease, the next sweep renews it and delivers the Cancel without waiting; env-3 waits.
        clock.Advance(TimeSpan.FromSeconds(10));
        await transport.SendAsync(ExecutionId, CancelEnvelope(ExecutionId, "env-2", clock.GetUtcNow()), clock.GetUtcNow());
        await transport.SendAsync(ExecutionId, NodeHarness.Envelope(ExecutionId, "env-3", clock.GetUtcNow()), clock.GetUtcNow());
        var beforeSecond = clock.GetUtcNow();
        var second = await SweepAdvancingAsync(node, clock);
        Assert.Equal((1, 1, 0), (second.RenewedCount, second.DispatchedCommandCount, second.AckedCount));
        Assert.Equal(beforeSecond, clock.GetUtcNow());

        // env-1's transport lease lapses while the placement is still live: it is leased again but not dispatched twice.
        clock.Advance(TimeSpan.FromSeconds(25));
        var third = await SweepAdvancingAsync(node, clock);
        Assert.Equal((1, 0), (third.RenewedCount, third.DispatchedCommandCount));
        Assert.Equal(NodeA, (await node.PlacementService.FindOwnerAsync(ExecutionId))!.OwnerId);

        // The drain ends: env-1 and the Cancel are acknowledged under their latest leases, and the held env-3 is released.
        executor.Release.SetResult();
        await WaitUntilAsync(async () => (await transport.ListLeasedAsync(NodeA, clock.GetUtcNow(), 10)).Count == 0);
        Assert.Equal(["env-1", "env-2"], executor.Processed);
        Assert.Equal(1, await transport.CountPendingAsync(ExecutionId));

        // The next sweep runs env-3 inline, without waiting for its lease to lapse.
        var fourth = await SweepAdvancingAsync(node, clock);
        Assert.Equal((1, 1), (fourth.DispatchedCommandCount, fourth.AckedCount));
        Assert.Equal(["env-1", "env-2", "env-3"], executor.Processed);
        Assert.Equal(0, await transport.CountPendingAsync(ExecutionId));
        Assert.Empty(await transport.ListLeasedAsync(NodeA, clock.GetUtcNow(), 10));
    }

    /// <summary>
    /// A renewal that finds the placement taken passivates the local actor, which waits for the running drain's turn. The
    /// sweep must not wait that out (#2831). Until the passivation ends, later sweeps leave the execution alone: a
    /// command leased meanwhile would wait for the passivation, then run on a fresh actor.
    /// </summary>
    [Fact]
    public async Task Passivating_a_busy_actor_after_a_lost_renewal_does_not_hold_the_sweep()
    {
        var store = new RenewalRefusingStore(new InMemoryExecutionPlacementStore());
        var transport = new InMemoryExecutionCommandTransport();
        var clock = new FakeTimeProvider(_now);
        var executor = new HoldingCommandExecutor("env-1");
        var node = new NodeHarness(NodeA, store, transport, clock, executor, LeaseDuration);
        await transport.SendAsync(ExecutionId, NodeHarness.Envelope(ExecutionId, "env-1", _now), _now);
        await SweepAdvancingAsync(node, clock);
        Assert.True(executor.Holding.Task.IsCompleted);

        // The lease is still listed as this node's, but its renewal loses (as to a reclaim racing it).
        store.RefuseRenewals = true;
        clock.Advance(TimeSpan.FromSeconds(10));
        var result = await SweepAdvancingAsync(node, clock);
        Assert.Equal(0, result.RenewedCount);

        await transport.SendAsync(ExecutionId, NodeHarness.Envelope(ExecutionId, "env-2", clock.GetUtcNow()), clock.GetUtcNow());
        var next = await SweepAdvancingAsync(node, clock);
        Assert.Equal((0, 0), (next.ClaimedCount, next.DispatchedCommandCount));
        Assert.Equal(["env-1"], (await transport.ListLeasedAsync(NodeA, clock.GetUtcNow(), 10)).Select(item => item.Envelope.EnvelopeId));
        executor.Release.SetResult();
    }

    /// <summary>Forwards to a placement store, but can make every renewal lose.</summary>
    private sealed class RenewalRefusingStore(IExecutionPlacementStore inner) : IExecutionPlacementStore
    {
        public bool RefuseRenewals { get; set; }

        public ValueTask<ExecutionPlacementLease?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            inner.FindAsync(workflowExecutionId, cancellationToken);

        public ValueTask<ExecutionPlacementClaimResult> TryClaimAsync(ExecutionPlacementClaim claim, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            inner.TryClaimAsync(claim, now, cancellationToken);

        public ValueTask<ExecutionPlacementLease?> TryRenewAsync(ExecutionPlacementLease held, DateTimeOffset now, DateTimeOffset expiresAt, CancellationToken cancellationToken = default) =>
            RefuseRenewals ? ValueTask.FromResult<ExecutionPlacementLease?>(null) : inner.TryRenewAsync(held, now, expiresAt, cancellationToken);

        public ValueTask<bool> ReleaseAsync(ExecutionPlacementLease lease, CancellationToken cancellationToken = default) =>
            inner.ReleaseAsync(lease, cancellationToken);

        public ValueTask<IReadOnlyList<ExecutionPlacementLease>> ListOwnedAsync(ExecutionPlacementLeaseListRequest request, CancellationToken cancellationToken = default) =>
            inner.ListOwnedAsync(request, cancellationToken);
    }

    /// <summary>A drain that fails still releases the commands held behind it; the failed command keeps its lease.</summary>
    [Fact]
    public async Task A_failed_drain_releases_the_commands_held_behind_it()
    {
        var store = new InMemoryExecutionPlacementStore();
        var transport = new InMemoryExecutionCommandTransport();
        var clock = new FakeTimeProvider(_now);
        var executor = new HoldingCommandExecutor("env-1") { FailHeld = true };
        var node = new NodeHarness(NodeA, store, transport, clock, executor, LeaseDuration);
        await transport.SendAsync(ExecutionId, NodeHarness.Envelope(ExecutionId, "env-1", _now), _now);
        await SweepAdvancingAsync(node, clock);

        await transport.SendAsync(ExecutionId, NodeHarness.Envelope(ExecutionId, "env-2", clock.GetUtcNow()), clock.GetUtcNow());
        var held = await SweepAdvancingAsync(node, clock);
        Assert.Equal(0, held.DispatchedCommandCount);
        Assert.Equal(2, (await transport.ListLeasedAsync(NodeA, clock.GetUtcNow(), 10)).Count);

        executor.Release.SetResult();
        await WaitUntilAsync(async () => (await transport.ListLeasedAsync(NodeA, clock.GetUtcNow(), 10)).Count == 1);
        Assert.Equal(["env-1"], (await transport.ListLeasedAsync(NodeA, clock.GetUtcNow(), 10)).Select(item => item.Envelope.EnvelopeId));
    }

    private static WorkflowExecutionCommandEnvelope CancelEnvelope(string executionId, string envelopeId, DateTimeOffset now) =>
        new(
            envelopeId: envelopeId,
            workflowExecutionId: executionId,
            command: new WorkflowExecutionCommand($"cmd-{envelopeId}", executionId, WorkflowExecutionCommandKind.Cancel, now, null, new Dictionary<string, string>()),
            idempotencyKey: $"idem-{envelopeId}",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: now);

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!await condition())
        {
            Assert.False(timeout.IsCancellationRequested, "The detached dispatches were not acknowledged.");
            await Task.Delay(10);
        }
    }

    // Fires the sweep's dispatch wait until the sweep returns. A sweep that waits out a held drain never returns.
    private static async Task<ExecutionPlacementSweepResult> SweepAdvancingAsync(NodeHarness node, FakeTimeProvider clock)
    {
        var sweep = node.Pump.SweepOnceAsync().AsTask();
        for (var attempt = 0; attempt < 500 && !sweep.IsCompleted; attempt++)
        {
            await Task.Delay(10);
            if (!sweep.IsCompleted)
                clock.Advance(TimeSpan.FromMilliseconds(100));
        }
        Assert.True(sweep.IsCompleted, "The sweep waited for a drain that keeps running.");
        return await sweep;
    }

    /// <summary>Holds one envelope's drain until released; records every envelope it runs.</summary>
    private sealed class HoldingCommandExecutor(string heldEnvelopeId) : IWorkflowExecutionCommandExecutor
    {
        private readonly ConcurrentQueue<string> _processed = new();

        public TaskCompletionSource Holding { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<string> Processed => _processed.ToArray();
        public bool FailHeld { get; init; }

        public async ValueTask<WorkflowExecutionCommandProcessResult> ProcessAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
        {
            _processed.Enqueue(envelope.EnvelopeId);
            if (envelope.EnvelopeId == heldEnvelopeId)
            {
                Holding.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
                if (FailHeld)
                    throw new InvalidOperationException("The held drain failed.");
            }
            return WorkflowExecutionCommandProcessResult.NoDrain;
        }
    }
}
