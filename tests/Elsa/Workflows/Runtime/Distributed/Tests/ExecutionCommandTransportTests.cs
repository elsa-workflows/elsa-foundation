using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Tests;

public sealed class ExecutionCommandTransportTests
{
    private const string ExecutionId = "wf-1";
    private const string NodeA = "node-a";
    private const string NodeB = "node-b";
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);
    private readonly DateTimeOffset _now = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Send_ThenLease_ReturnsItemsInEnqueueOrder()
    {
        var transport = new InMemoryExecutionCommandTransport();
        await transport.SendAsync(ExecutionId, Envelope("env-1"), _now);
        await transport.SendAsync(ExecutionId, Envelope("env-2"), _now);

        var leased = await transport.LeaseAsync(ExecutionId, NodeA, _now, LeaseDuration, maxItems: 10);

        Assert.Equal(new[] { "env-1", "env-2" }, leased.Select(i => i.Envelope.EnvelopeId));
        Assert.All(leased, i => Assert.Equal(NodeA, i.LeasedByOwnerId));
        Assert.All(leased, i => Assert.Equal(1, i.DeliveryAttemptCount));
    }

    [Fact]
    public async Task Lease_HidesItemsFromOtherNodesUntilExpiry()
    {
        var transport = new InMemoryExecutionCommandTransport();
        await transport.SendAsync(ExecutionId, Envelope("env-1"), _now);

        var leasedByA = await transport.LeaseAsync(ExecutionId, NodeA, _now, LeaseDuration, maxItems: 10);
        Assert.Single(leasedByA);

        // While node A's lease is live, node B sees nothing.
        var leasedByB = await transport.LeaseAsync(ExecutionId, NodeB, _now, LeaseDuration, maxItems: 10);
        Assert.Empty(leasedByB);

        // After the lease expires, node B can re-lease it (failover re-drive).
        var afterExpiry = _now + LeaseDuration + TimeSpan.FromSeconds(1);
        var reLeasedByB = await transport.LeaseAsync(ExecutionId, NodeB, afterExpiry, LeaseDuration, maxItems: 10);
        Assert.Single(reLeasedByB);
        Assert.Equal(NodeB, reLeasedByB[0].LeasedByOwnerId);
        Assert.Equal(2, reLeasedByB[0].DeliveryAttemptCount);
    }

    [Fact]
    public async Task Ack_ByLeaseHolder_RemovesItem()
    {
        var transport = new InMemoryExecutionCommandTransport();
        await transport.SendAsync(ExecutionId, Envelope("env-1"), _now);
        var leased = await transport.LeaseAsync(ExecutionId, NodeA, _now, LeaseDuration, maxItems: 10);

        var acked = await transport.AckAsync(ExecutionId, leased[0].TransportItemId, NodeA, leased[0].LeaseToken!.Value, _now);

        Assert.True(acked);
        Assert.Equal(0, await transport.CountPendingAsync(ExecutionId));
    }

    [Fact]
    public async Task Ack_BySupersededNode_AfterFailover_IsRefused()
    {
        var transport = new InMemoryExecutionCommandTransport();
        await transport.SendAsync(ExecutionId, Envelope("env-1"), _now);

        // Node A leases, then dies (never acks). The lease expires and node B re-leases.
        var leasedByA = await transport.LeaseAsync(ExecutionId, NodeA, _now, LeaseDuration, maxItems: 10);
        var afterExpiry = _now + LeaseDuration + TimeSpan.FromSeconds(1);
        var leasedByB = await transport.LeaseAsync(ExecutionId, NodeB, afterExpiry, LeaseDuration, maxItems: 10);
        Assert.Single(leasedByB);

        // Node A resurrects and tries to ack the item it leased before dying — it must be refused, because node B now
        // holds the lease and is responsible for re-driving the command (at-least-once re-delivery on failover).
        var staleAck = await transport.AckAsync(ExecutionId, leasedByA[0].TransportItemId, NodeA, leasedByA[0].LeaseToken!.Value, afterExpiry);
        Assert.False(staleAck);
        Assert.Equal(1, await transport.CountPendingAsync(ExecutionId));

        // Node B, the live lease holder, can ack.
        var liveAck = await transport.AckAsync(ExecutionId, leasedByB[0].TransportItemId, NodeB, leasedByB[0].LeaseToken!.Value, afterExpiry);
        Assert.True(liveAck);
        Assert.Equal(0, await transport.CountPendingAsync(ExecutionId));
    }

    [Fact]
    public async Task ListPendingExecutionIds_ReturnsOnlyExecutionsWithVisibleItems()
    {
        var transport = new InMemoryExecutionCommandTransport();
        await transport.SendAsync("wf-a", Envelope("env-a", "wf-a"), _now);
        await transport.SendAsync("wf-b", Envelope("env-b", "wf-b"), _now);

        // Lease wf-a so it is no longer visible; wf-b remains visible.
        await transport.LeaseAsync("wf-a", NodeA, _now, LeaseDuration, maxItems: 10);

        var pending = await transport.ListPendingExecutionIdsAsync(_now, 10);
        Assert.Equal(new[] { "wf-b" }, pending);

        // After wf-a's lease expires it is pending again.
        var afterExpiry = _now + LeaseDuration + TimeSpan.FromSeconds(1);
        var pendingAfter = await transport.ListPendingExecutionIdsAsync(afterExpiry, 10);
        Assert.Equal(new[] { "wf-a", "wf-b" }, pendingAfter);
    }

    [Fact]
    public async Task Lease_RespectsMaxItems()
    {
        var transport = new InMemoryExecutionCommandTransport();
        for (var i = 0; i < 5; i++)
            await transport.SendAsync(ExecutionId, Envelope($"env-{i}"), _now);

        var leased = await transport.LeaseAsync(ExecutionId, NodeA, _now, LeaseDuration, maxItems: 2);
        Assert.Equal(2, leased.Count);
        Assert.Equal(new[] { "env-0", "env-1" }, leased.Select(i => i.Envelope.EnvelopeId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public async Task BoundedOperations_RejectLimitsOutsideTheDeclaredMaximum(int maxItems)
    {
        var transport = new InMemoryExecutionCommandTransport();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await transport.LeaseAsync(ExecutionId, NodeA, _now, LeaseDuration, maxItems));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await transport.ListPendingExecutionIdsAsync(_now, maxItems));
    }

    private WorkflowExecutionCommandEnvelope Envelope(string envelopeId, string executionId = ExecutionId)
    {
        var command = new WorkflowExecutionCommand(
            CommandId: $"cmd-{envelopeId}",
            WorkflowExecutionId: executionId,
            Kind: WorkflowExecutionCommandKind.RunSchedulerWork,
            EnqueuedAt: _now,
            Payload: null,
            Metadata: new Dictionary<string, string>());

        return new WorkflowExecutionCommandEnvelope(
            envelopeId: envelopeId,
            workflowExecutionId: executionId,
            command: command,
            idempotencyKey: $"idem-{envelopeId}",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: _now);
    }
}
