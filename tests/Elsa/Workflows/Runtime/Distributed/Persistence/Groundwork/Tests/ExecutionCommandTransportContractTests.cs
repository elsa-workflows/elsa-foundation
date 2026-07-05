using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests;

/// <summary>
/// Contract-parity suite for <c>IExecutionCommandTransport</c>: the same at-least-once, ack-based lease/visibility
/// assertions run against the leaf's in-memory transport, the Groundwork bridge over the shared in-memory
/// document-store double, and the Groundwork bridge over the real SQLite provider.
/// </summary>
public sealed class ExecutionCommandTransportContractTests
{
    private const string ExecutionId = "wf-1";
    private const string NodeA = "node-a";
    private const string NodeB = "node-b";

    private static readonly DateTimeOffset Now = new(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);

    public static TheoryData<string> Providers => new()
    {
        DistributedStoreHarness.InMemory,
        DistributedStoreHarness.GroundworkMemory,
        DistributedStoreHarness.GroundworkSqlite
    };

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Send_AssignsStrictlyIncreasingSequences_AndLeaseDrainsInEnqueueOrder(string provider)
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(provider);

        var first = await harness.Transport.SendAsync(ExecutionId, DistributedStoreHarness.Envelope(ExecutionId, "env-1", Now), Now);
        var second = await harness.Transport.SendAsync(ExecutionId, DistributedStoreHarness.Envelope(ExecutionId, "env-2", Now), Now);

        Assert.True(second.Sequence > first.Sequence);

        var leased = await harness.Transport.LeaseAsync(ExecutionId, NodeA, Now, LeaseDuration, maxItems: 10);
        Assert.Equal(new[] { "env-1", "env-2" }, leased.Select(item => item.Envelope.EnvelopeId).ToArray());
        Assert.All(leased, item => Assert.Equal(NodeA, item.LeasedByOwnerId));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Lease_HidesItemsFromOtherNodes_UntilExpiry(string provider)
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(provider);
        await harness.Transport.SendAsync(ExecutionId, DistributedStoreHarness.Envelope(ExecutionId, "env-1", Now), Now);

        var leasedByA = await harness.Transport.LeaseAsync(ExecutionId, NodeA, Now, LeaseDuration, maxItems: 10);
        Assert.Single(leasedByA);

        // Hidden while node A's visibility lease is live.
        Assert.Empty(await harness.Transport.LeaseAsync(ExecutionId, NodeB, Now.AddSeconds(1), LeaseDuration, maxItems: 10));
        Assert.Empty(await harness.Transport.ListPendingExecutionIdsAsync(Now.AddSeconds(1)));

        // Visible again after expiry: the survivor re-leases and the delivery attempt count grows.
        var afterExpiry = Now + LeaseDuration + TimeSpan.FromSeconds(1);
        Assert.Equal(new[] { ExecutionId }, await harness.Transport.ListPendingExecutionIdsAsync(afterExpiry));

        var releasedToB = await harness.Transport.LeaseAsync(ExecutionId, NodeB, afterExpiry, LeaseDuration, maxItems: 10);
        Assert.Single(releasedToB);
        Assert.Equal(NodeB, releasedToB[0].LeasedByOwnerId);
        Assert.Equal(2, releasedToB[0].DeliveryAttemptCount);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Ack_ByLiveLeaseHolder_RemovesTheItem(string provider)
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(provider);
        await harness.Transport.SendAsync(ExecutionId, DistributedStoreHarness.Envelope(ExecutionId, "env-1", Now), Now);
        var leased = await harness.Transport.LeaseAsync(ExecutionId, NodeA, Now, LeaseDuration, maxItems: 10);

        Assert.True(await harness.Transport.AckAsync(ExecutionId, leased[0].TransportItemId, NodeA, Now.AddSeconds(1)));
        Assert.Equal(0, await harness.Transport.CountPendingAsync(ExecutionId));
        Assert.Empty(await harness.Transport.ListPendingExecutionIdsAsync(Now.AddSeconds(1)));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Ack_ByNonHolderOrAfterExpiry_IsRefused(string provider)
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(provider);
        await harness.Transport.SendAsync(ExecutionId, DistributedStoreHarness.Envelope(ExecutionId, "env-1", Now), Now);
        var leased = await harness.Transport.LeaseAsync(ExecutionId, NodeA, Now, LeaseDuration, maxItems: 10);
        var itemId = leased[0].TransportItemId;

        // A node that never held the lease cannot ack.
        Assert.False(await harness.Transport.AckAsync(ExecutionId, itemId, NodeB, Now.AddSeconds(1)));

        // The holder itself cannot ack after its lease expired — the survivor is now responsible.
        var afterExpiry = Now + LeaseDuration + TimeSpan.FromSeconds(1);
        Assert.False(await harness.Transport.AckAsync(ExecutionId, itemId, NodeA, afterExpiry));
        Assert.Equal(1, await harness.Transport.CountPendingAsync(ExecutionId));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Ack_BySupersededHolder_AfterSurvivorReleased_IsRefused(string provider)
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(provider);
        await harness.Transport.SendAsync(ExecutionId, DistributedStoreHarness.Envelope(ExecutionId, "env-1", Now), Now);
        var leasedByA = await harness.Transport.LeaseAsync(ExecutionId, NodeA, Now, LeaseDuration, maxItems: 10);

        var afterExpiry = Now + LeaseDuration + TimeSpan.FromSeconds(1);
        var leasedByB = await harness.Transport.LeaseAsync(ExecutionId, NodeB, afterExpiry, LeaseDuration, maxItems: 10);
        Assert.Single(leasedByB);

        // The dead node "wakes up" and tries to ack away the command the survivor now owns.
        Assert.False(await harness.Transport.AckAsync(ExecutionId, leasedByA[0].TransportItemId, NodeA, afterExpiry.AddSeconds(1)));
        Assert.True(await harness.Transport.AckAsync(ExecutionId, leasedByB[0].TransportItemId, NodeB, afterExpiry.AddSeconds(1)));
        Assert.Equal(0, await harness.Transport.CountPendingAsync(ExecutionId));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Lease_RespectsMaxItems_AndListPendingSpansExecutions(string provider)
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(provider);
        await harness.Transport.SendAsync("wf-b", DistributedStoreHarness.Envelope("wf-b", "env-1", Now), Now);
        await harness.Transport.SendAsync("wf-b", DistributedStoreHarness.Envelope("wf-b", "env-2", Now), Now);
        await harness.Transport.SendAsync("wf-a", DistributedStoreHarness.Envelope("wf-a", "env-3", Now), Now);

        Assert.Equal(new[] { "wf-a", "wf-b" }, await harness.Transport.ListPendingExecutionIdsAsync(Now));

        var leased = await harness.Transport.LeaseAsync("wf-b", NodeA, Now, LeaseDuration, maxItems: 1);
        Assert.Single(leased);
        Assert.Equal("env-1", leased[0].Envelope.EnvelopeId);
        Assert.Equal(2, await harness.Transport.CountPendingAsync("wf-b"));
    }
}
