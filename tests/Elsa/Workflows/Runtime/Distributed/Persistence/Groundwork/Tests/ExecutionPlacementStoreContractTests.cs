using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests;

/// <summary>
/// Contract-parity suite for <c>IExecutionPlacementStore</c>: the same behavioral assertions run against the leaf's
/// in-memory store, the Groundwork bridge over the shared in-memory document-store double, and the Groundwork
/// bridge over the real SQLite provider — proving the durable stores are true drop-ins behind the frozen contract.
/// </summary>
public sealed class ExecutionPlacementStoreContractTests
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
    public async Task Claim_OnUnplacedExecution_GrantsWithTokenOne(string provider)
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(provider);

        var result = await harness.PlacementStore.TryClaimAsync(Claim(NodeA, Now), Now);

        Assert.Equal(ExecutionPlacementClaimOutcome.Granted, result.Outcome);
        Assert.Equal(NodeA, result.Lease.OwnerId);
        Assert.Equal(1, result.Lease.PlacementToken);

        var found = await harness.PlacementStore.FindAsync(ExecutionId);
        Assert.Equal(NodeA, found!.OwnerId);
        Assert.Equal(1, found.PlacementToken);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Claim_ByCurrentOwner_RenewsWithStrictlyGreaterToken(string provider)
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(provider);
        await harness.PlacementStore.TryClaimAsync(Claim(NodeA, Now), Now);

        var later = Now.AddSeconds(10);
        var renewal = await harness.PlacementStore.TryClaimAsync(Claim(NodeA, later), later);

        Assert.Equal(ExecutionPlacementClaimOutcome.Renewed, renewal.Outcome);
        Assert.Equal(NodeA, renewal.Lease.OwnerId);
        Assert.Equal(2, renewal.Lease.PlacementToken);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Claim_AgainstLiveForeignLease_IsDeniedWithoutMutation(string provider)
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(provider);
        var granted = await harness.PlacementStore.TryClaimAsync(Claim(NodeA, Now), Now);

        var denied = await harness.PlacementStore.TryClaimAsync(Claim(NodeB, Now.AddSeconds(1)), Now.AddSeconds(1));

        Assert.Equal(ExecutionPlacementClaimOutcome.Denied, denied.Outcome);
        Assert.Equal(NodeA, denied.Lease.OwnerId);
        Assert.Equal(granted.Lease.PlacementToken, denied.Lease.PlacementToken);

        var found = await harness.PlacementStore.FindAsync(ExecutionId);
        Assert.Equal(NodeA, found!.OwnerId);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Claim_AgainstExpiredForeignLease_GrantsWithGreaterToken(string provider)
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(provider);
        await harness.PlacementStore.TryClaimAsync(Claim(NodeA, Now), Now);

        var afterExpiry = Now + LeaseDuration + TimeSpan.FromSeconds(1);
        var takeover = await harness.PlacementStore.TryClaimAsync(Claim(NodeB, afterExpiry), afterExpiry);

        Assert.Equal(ExecutionPlacementClaimOutcome.Granted, takeover.Outcome);
        Assert.Equal(NodeB, takeover.Lease.OwnerId);
        Assert.Equal(2, takeover.Lease.PlacementToken);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Release_MatchingOwnerAndToken_Unplaces(string provider)
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(provider);
        var granted = await harness.PlacementStore.TryClaimAsync(Claim(NodeA, Now), Now);

        await harness.PlacementStore.ReleaseAsync(granted.Lease);

        Assert.Null(await harness.PlacementStore.FindAsync(ExecutionId));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Release_WithStaleToken_IsANoOp(string provider)
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(provider);
        var first = await harness.PlacementStore.TryClaimAsync(Claim(NodeA, Now), Now);
        var renewed = await harness.PlacementStore.TryClaimAsync(Claim(NodeA, Now.AddSeconds(5)), Now.AddSeconds(5));

        // The stale (superseded) lease must not clear the newer one.
        await harness.PlacementStore.ReleaseAsync(first.Lease);

        var found = await harness.PlacementStore.FindAsync(ExecutionId);
        Assert.Equal(renewed.Lease.PlacementToken, found!.PlacementToken);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task List_ReturnsAllStoredLeases_OrderedByExecutionId(string provider)
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(provider);
        await harness.PlacementStore.TryClaimAsync(Claim(NodeA, Now, "wf-b"), Now);
        await harness.PlacementStore.TryClaimAsync(Claim(NodeB, Now, "wf-a"), Now);

        var leases = await harness.PlacementStore.ListAsync();

        Assert.Equal(new[] { "wf-a", "wf-b" }, leases.Select(lease => lease.WorkflowExecutionId).ToArray());
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task ListPage_ReturnsBoundedWindowAndTotalCount(string provider)
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(provider);
        await harness.PlacementStore.TryClaimAsync(Claim(NodeA, Now, "wf-a"), Now);
        await harness.PlacementStore.TryClaimAsync(Claim(NodeA, Now, "wf-b"), Now);
        await harness.PlacementStore.TryClaimAsync(Claim(NodeA, Now, "wf-c"), Now);
        var paged = Assert.IsAssignableFrom<IPagedExecutionPlacementStore>(harness.PlacementStore);

        var page = await paged.ListPageAsync(new ExecutionPlacementLeasePageRequest(1, 1));

        Assert.Equal(3, page.TotalCount);
        Assert.Equal("wf-b", Assert.Single(page.Items).WorkflowExecutionId);
    }

    private static ExecutionPlacementClaim Claim(string ownerId, DateTimeOffset requestedAt, string executionId = ExecutionId) =>
        new(executionId, ownerId, requestedAt, requestedAt + LeaseDuration);
}
