using Elsa.Workflows.Runtime.Distributed.Contracts;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests;

/// <summary>Runs the command transport contract against the product in-memory store and the public v2 SQLite adapter.</summary>
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
        DistributedStoreHarness.GroundworkSqlite
    };

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task SendSequencesStrictlyIncreaseAndLeaseDrainsInOrder(string provider)
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(provider);
        var first = await harness.Transport.SendAsync(ExecutionId, Envelope("env-1"), Now);
        var second = await harness.Transport.SendAsync(ExecutionId, Envelope("env-2"), Now);

        var leased = await harness.Transport.LeaseAsync(ExecutionId, NodeA, Now, LeaseDuration, 10);

        Assert.True(second.Sequence > first.Sequence);
        Assert.Equal(["env-1", "env-2"], leased.Select(item => item.Envelope.EnvelopeId));
        Assert.All(leased, item => Assert.Equal(NodeA, item.LeasedByOwnerId));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task LeaseHidesUntilExpiryThenIncrementsDeliveryAttempt(string provider)
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(provider);
        await harness.Transport.SendAsync(ExecutionId, Envelope("env-1"), Now);
        Assert.Single(await harness.Transport.LeaseAsync(ExecutionId, NodeA, Now, LeaseDuration, 10));

        Assert.Empty(await harness.Transport.LeaseAsync(ExecutionId, NodeB, Now.AddSeconds(1), LeaseDuration, 10));
        Assert.Empty(await harness.Transport.ListPendingExecutionIdsAsync(Now.AddSeconds(1), 10));

        var afterExpiry = Now + LeaseDuration + TimeSpan.FromSeconds(1);
        Assert.Equal([ExecutionId], await harness.Transport.ListPendingExecutionIdsAsync(afterExpiry, 10));
        var released = await harness.Transport.LeaseAsync(ExecutionId, NodeB, afterExpiry, LeaseDuration, 10);
        Assert.Equal(2, Assert.Single(released).DeliveryAttemptCount);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task AckRequiresTheCurrentLiveHolderAndDeletesExactlyOneItem(string provider)
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(provider);
        await harness.Transport.SendAsync(ExecutionId, Envelope("env-1"), Now);
        var item = Assert.Single(await harness.Transport.LeaseAsync(ExecutionId, NodeA, Now, LeaseDuration, 10));

        Assert.False(await harness.Transport.AckAsync(ExecutionId, item.TransportItemId, NodeB, item.LeaseToken!.Value, Now.AddSeconds(1)));
        Assert.True(await harness.Transport.AckAsync(ExecutionId, item.TransportItemId, NodeA, item.LeaseToken.Value, Now.AddSeconds(1)));
        Assert.Equal(0, await harness.Transport.CountPendingAsync(ExecutionId));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task ExpiredOrSupersededHolderCannotAck(string provider)
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(provider);
        await harness.Transport.SendAsync(ExecutionId, Envelope("env-1"), Now);
        var first = Assert.Single(await harness.Transport.LeaseAsync(ExecutionId, NodeA, Now, LeaseDuration, 10));
        var afterExpiry = Now + LeaseDuration + TimeSpan.FromSeconds(1);
        Assert.False(await harness.Transport.AckAsync(ExecutionId, first.TransportItemId, NodeA, first.LeaseToken!.Value, afterExpiry));

        var second = Assert.Single(await harness.Transport.LeaseAsync(ExecutionId, NodeB, afterExpiry, LeaseDuration, 10));
        Assert.False(await harness.Transport.AckAsync(ExecutionId, first.TransportItemId, NodeA, first.LeaseToken.Value, afterExpiry.AddSeconds(1)));
        Assert.True(await harness.Transport.AckAsync(ExecutionId, second.TransportItemId, NodeB, second.LeaseToken!.Value, afterExpiry.AddSeconds(1)));
    }

    [Fact]
    public async Task DurableAdapterReopenReleasesExpiredItemAndRejectsStaleToken()
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(DistributedStoreHarness.GroundworkSqlite);
        await harness.Transport.SendAsync(ExecutionId, Envelope("env-1"), Now);
        var before = Assert.Single(await harness.Transport.LeaseAsync(ExecutionId, NodeA, Now, LeaseDuration, 10));
        var afterExpiry = Now + LeaseDuration + TimeSpan.FromSeconds(1);

        var reopened = await harness.ReopenTransportAsync();
        var after = Assert.Single(await reopened.LeaseAsync(ExecutionId, NodeA, afterExpiry, LeaseDuration, 10));

        Assert.NotEqual(before.LeaseToken, after.LeaseToken);
        Assert.False(await reopened.AckAsync(ExecutionId, before.TransportItemId, NodeA, before.LeaseToken!.Value, afterExpiry.AddSeconds(1)));
        Assert.True(await reopened.AckAsync(ExecutionId, after.TransportItemId, NodeA, after.LeaseToken!.Value, afterExpiry.AddSeconds(1)));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task LeaseAndPendingQueriesRespectBoundsAcrossExecutions(string provider)
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(provider);
        await harness.Transport.SendAsync("wf-a", Envelope("env-1", "wf-a"), Now);
        await harness.Transport.SendAsync("wf-a", Envelope("env-2", "wf-a"), Now);
        await harness.Transport.SendAsync("wf-b", Envelope("env-3", "wf-b"), Now);

        Assert.Equal(["wf-a"], await harness.Transport.ListPendingExecutionIdsAsync(Now, 1));
        Assert.Equal(["wf-a", "wf-b"], await harness.Transport.ListPendingExecutionIdsAsync(Now, 2));
        Assert.Single(await harness.Transport.LeaseAsync("wf-a", NodeA, Now, LeaseDuration, 1));
        Assert.Equal(2, await harness.Transport.CountPendingAsync("wf-a"));
    }

    [Fact]
    public async Task SqlitePreservesMaximumIdentityAndOrdinalUnicodeOrdering()
    {
        await using var harness = await DistributedStoreHarness.CreateAsync(DistributedStoreHarness.GroundworkSqlite);
        var supplementary = "wf-\U00010000";
        var privateUse = "wf-\uE000";
        var maximumLength = new string('x', DistributedRuntimeIdentityConstraints.MaximumLength - 3) + "\U0001F600:";
        foreach (var executionId in new[] { privateUse, maximumLength, supplementary })
            await harness.Transport.SendAsync(executionId, Envelope($"env-{executionId.Length}", executionId), Now);

        var expected = new[] { privateUse, maximumLength, supplementary }
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected, await harness.Transport.ListPendingExecutionIdsAsync(Now, 10));
    }

    private static Elsa.Workflows.Runtime.Core.Models.WorkflowExecutionCommandEnvelope Envelope(
        string envelopeId,
        string executionId = ExecutionId) =>
        DistributedStoreHarness.Envelope(executionId, envelopeId, Now);
}
