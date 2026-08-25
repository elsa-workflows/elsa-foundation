using Elsa.Workflows.Runtime.Distributed.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests;

/// <summary>Runs the placement contract against the product in-memory store and the public v2 SQLite adapter.</summary>
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
        DistributedStoreHarness.GroundworkSqlite
    };

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task ClaimGrantRenewAndFindHaveContractParity(string provider)
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(provider);

        var granted = await harness.PlacementStore.TryClaimAsync(Claim(NodeA, Now), Now);
        var renewed = await harness.PlacementStore.TryClaimAsync(Claim(NodeA, Now.AddSeconds(10)), Now.AddSeconds(10));

        Assert.Equal(ExecutionPlacementClaimOutcome.Granted, granted.Outcome);
        Assert.Equal(1, granted.Lease.PlacementToken);
        Assert.Equal(ExecutionPlacementClaimOutcome.Renewed, renewed.Outcome);
        Assert.Equal(2, renewed.Lease.PlacementToken);
        var found = await harness.PlacementStore.FindAsync(ExecutionId);
        Assert.Equal(renewed.Lease.OwnerId, found!.OwnerId);
        Assert.Equal(renewed.Lease.PlacementToken, found.PlacementToken);
        Assert.Equal(renewed.Lease.ExpiresAt, found.ExpiresAt);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task LiveForeignClaimIsDeniedWithoutMutation(string provider)
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(provider);
        var granted = await harness.PlacementStore.TryClaimAsync(Claim(NodeA, Now), Now);

        var denied = await harness.PlacementStore.TryClaimAsync(Claim(NodeB, Now.AddSeconds(1)), Now.AddSeconds(1));

        Assert.Equal(ExecutionPlacementClaimOutcome.Denied, denied.Outcome);
        Assert.Equal(granted.Lease.OwnerId, denied.Lease.OwnerId);
        Assert.Equal(granted.Lease.PlacementToken, denied.Lease.PlacementToken);
        Assert.Equal(NodeA, (await harness.PlacementStore.FindAsync(ExecutionId))!.OwnerId);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task ExpiredForeignClaimTakesOverWithGreaterToken(string provider)
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
    public async Task MatchingReleaseDeletesButStaleReleaseDoesNot(string provider)
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(provider);
        var first = await harness.PlacementStore.TryClaimAsync(Claim(NodeA, Now), Now);
        var renewed = await harness.PlacementStore.TryClaimAsync(Claim(NodeA, Now.AddSeconds(5)), Now.AddSeconds(5));

        await harness.PlacementStore.ReleaseAsync(first.Lease);
        Assert.Equal(renewed.Lease.PlacementToken, (await harness.PlacementStore.FindAsync(ExecutionId))!.PlacementToken);

        await harness.PlacementStore.ReleaseAsync(renewed.Lease);
        Assert.Null(await harness.PlacementStore.FindAsync(ExecutionId));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task ListOwnedFiltersOwnerAndExpiryThenAppliesStableBound(string provider)
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(provider);
        await harness.PlacementStore.TryClaimAsync(Claim(NodeA, Now, "wf-later"), Now);
        await harness.PlacementStore.TryClaimAsync(new("wf-first", NodeA, Now, Now.AddSeconds(10)), Now);
        await harness.PlacementStore.TryClaimAsync(new("wf-expired", NodeA, Now, Now.AddSeconds(1)), Now);
        await harness.PlacementStore.TryClaimAsync(Claim(NodeB, Now, "wf-foreign"), Now);

        var leases = await harness.PlacementStore.ListOwnedAsync(new(NodeA, Now.AddSeconds(2), take: 1));

        Assert.Equal("wf-first", Assert.Single(leases).WorkflowExecutionId);
    }

    private static ExecutionPlacementClaim Claim(
        string ownerId,
        DateTimeOffset requestedAt,
        string executionId = ExecutionId) =>
        new(executionId, ownerId, requestedAt, requestedAt + LeaseDuration);
}
