using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Elsa.Workflows.Runtime.Distributed.Options;
using Elsa.Workflows.Runtime.Distributed.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Tests;

public sealed class ExecutionPlacementTests
{
    private const string ExecutionId = "wfexec-1";
    private const string NodeA = "node-a";
    private const string NodeB = "node-b";
    private readonly DateTimeOffset _now = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TryClaim_OnUnplacedExecution_GrantsWithFirstToken()
    {
        var store = new InMemoryExecutionPlacementStore();
        var service = NewService(store, NodeA, out _);

        var result = await service.TryClaimAsync(ExecutionId);

        Assert.Equal(ExecutionPlacementClaimOutcome.Granted, result.Outcome);
        Assert.True(result.IsOwnedByClaimant);
        Assert.Equal(NodeA, result.Lease.OwnerId);
        Assert.Equal(1, result.Lease.PlacementToken);
    }

    [Fact]
    public async Task TryClaim_BySameNode_RenewsAndBumpsToken()
    {
        var store = new InMemoryExecutionPlacementStore();
        var service = NewService(store, NodeA, out _);

        var first = await service.TryClaimAsync(ExecutionId);
        var second = await service.TryClaimAsync(ExecutionId);

        Assert.Equal(ExecutionPlacementClaimOutcome.Granted, first.Outcome);
        Assert.Equal(ExecutionPlacementClaimOutcome.Renewed, second.Outcome);
        Assert.Equal(2, second.Lease.PlacementToken);
    }

    [Fact]
    public async Task TryClaim_ByOtherNode_WhileLive_IsDenied_AndKeepsCurrentOwner()
    {
        var store = new InMemoryExecutionPlacementStore();
        var nodeA = NewService(store, NodeA, out _);
        var nodeB = NewService(store, NodeB, out _);

        await nodeA.TryClaimAsync(ExecutionId);
        var denied = await nodeB.TryClaimAsync(ExecutionId);

        Assert.Equal(ExecutionPlacementClaimOutcome.Denied, denied.Outcome);
        Assert.False(denied.IsOwnedByClaimant);
        Assert.Equal(NodeA, denied.Lease.OwnerId);
    }

    [Fact]
    public async Task TryClaim_ByOtherNode_AfterExpiry_TakesOverWithGreaterToken()
    {
        var store = new InMemoryExecutionPlacementStore();
        var clock = new MutableTimeProvider(_now);
        var nodeA = NewService(store, NodeA, clock);
        var nodeB = NewService(store, NodeB, clock);

        var aLease = await nodeA.TryClaimAsync(ExecutionId);

        // Node A stops renewing; advance past the lease duration so its placement expires.
        clock.Advance(TimeSpan.FromSeconds(31));

        var takeover = await nodeB.TryClaimAsync(ExecutionId);

        Assert.Equal(ExecutionPlacementClaimOutcome.Granted, takeover.Outcome);
        Assert.Equal(NodeB, takeover.Lease.OwnerId);
        Assert.True(takeover.Lease.PlacementToken > aLease.Lease.PlacementToken);
    }

    [Fact]
    public async Task Release_ByCurrentHolder_ClearsPlacement()
    {
        var store = new InMemoryExecutionPlacementStore();
        var service = NewService(store, NodeA, new MutableTimeProvider(_now));

        var lease = await service.TryClaimAsync(ExecutionId);
        await service.ReleaseAsync(lease.Lease);

        Assert.Null(await service.FindOwnerAsync(ExecutionId));
    }

    [Fact]
    public async Task Release_BySupersededHolder_DoesNotClearNewerOwner()
    {
        var store = new InMemoryExecutionPlacementStore();
        var clock = new MutableTimeProvider(_now);
        var nodeA = NewService(store, NodeA, clock);
        var nodeB = NewService(store, NodeB, clock);

        var stale = await nodeA.TryClaimAsync(ExecutionId);
        clock.Advance(TimeSpan.FromSeconds(31));
        var current = await nodeB.TryClaimAsync(ExecutionId);

        // The superseded holder (node A) must not be able to clear node B's live placement.
        await nodeA.ReleaseAsync(stale.Lease);

        var owner = await nodeB.FindOwnerAsync(ExecutionId);
        Assert.NotNull(owner);
        Assert.Equal(NodeB, owner!.OwnerId);
        Assert.Equal(current.Lease.PlacementToken, owner.PlacementToken);
    }

    [Fact]
    public async Task ListOwned_ReturnsOnlyThisNodesLiveLeases()
    {
        var store = new InMemoryExecutionPlacementStore();
        var clock = new MutableTimeProvider(_now);
        var nodeA = NewService(store, NodeA, clock);
        var nodeB = NewService(store, NodeB, clock);

        await nodeA.TryClaimAsync("wfexec-a1");
        await nodeA.TryClaimAsync("wfexec-a2");
        await nodeB.TryClaimAsync("wfexec-b1");

        var owned = await nodeA.ListOwnedAsync(10);
        Assert.Equal(new[] { "wfexec-a1", "wfexec-a2" }, owned.Select(l => l.WorkflowExecutionId).OrderBy(x => x));

        // Expired leases are not "owned".
        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Empty(await nodeA.ListOwnedAsync(10));
    }

    [Fact]
    public void Constructor_RejectsNonPositiveLeaseDuration()
    {
        var store = new InMemoryExecutionPlacementStore();
        var options = new ExecutionPlacementOptions { NodeId = NodeA, LeaseDuration = TimeSpan.Zero };
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExecutionPlacementService(store, new MutableTimeProvider(_now), options));
    }

    [Fact]
    public void Constructor_RejectsOwnerIdentifiersOutsideThePortableBoundary()
    {
        var store = new InMemoryExecutionPlacementStore();
        var options = new ExecutionPlacementOptions
        {
            NodeId = new string('x', DistributedRuntimeIdentityConstraints.MaximumLength + 1),
            LeaseDuration = TimeSpan.FromSeconds(30)
        };

        Assert.Throws<ArgumentException>(() =>
            new ExecutionPlacementService(store, new MutableTimeProvider(_now), options));
    }

    [Fact]
    public void IdentityBoundary_RejectsMalformedUnicodeAndAcceptsTheMaximumLength()
    {
        var maximum = new string('x', DistributedRuntimeIdentityConstraints.MaximumLength);

        Assert.Equal(
            maximum,
            DistributedRuntimeIdentityConstraints.Validate(maximum, "value"));
        Assert.Throws<ArgumentException>(() =>
            DistributedRuntimeIdentityConstraints.Validate("\ud800", "value"));
        Assert.Throws<ArgumentException>(() =>
            DistributedRuntimeIdentityConstraints.Validate(
                maximum + "x",
                "value"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public async Task ListOwned_RejectsLimitsOutsideTheDeclaredMaximum(int maxItems)
    {
        var service = NewService(new InMemoryExecutionPlacementStore(), NodeA, out _);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await service.ListOwnedAsync(maxItems));
    }

    private ExecutionPlacementService NewService(InMemoryExecutionPlacementStore store, string nodeId, out MutableTimeProvider clock)
    {
        clock = new MutableTimeProvider(_now);
        return NewService(store, nodeId, clock);
    }

    private static ExecutionPlacementService NewService(InMemoryExecutionPlacementStore store, string nodeId, MutableTimeProvider clock)
    {
        var options = new ExecutionPlacementOptions { NodeId = nodeId, LeaseDuration = TimeSpan.FromSeconds(30) };
        return new ExecutionPlacementService(store, clock, options);
    }
}
