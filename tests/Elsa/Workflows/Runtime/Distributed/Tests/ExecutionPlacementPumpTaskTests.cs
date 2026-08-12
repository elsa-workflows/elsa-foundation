using Elsa.Workflows.Runtime.Core.Models;
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
}
