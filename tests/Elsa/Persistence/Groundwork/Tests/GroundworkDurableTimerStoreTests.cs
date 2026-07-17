using System.Text.Json;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkDurableTimerStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    // The SAME contract assertions run against two host-selected providers (real Groundwork SQLite and an
    // in-memory document store). Identical behavior proves the bridge is provider-neutral.
    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Save_Find_RoundTripsTimer(string provider)
    {
        await using var fixture = CreateStore(provider);
        IDurableTimerStore store = NewStore(fixture);

        var saved = await store.SaveAsync(NewTimer("timer-1", dueOffset: TimeSpan.FromMinutes(5)));
        Assert.Equal("timer-1", saved.TimerId);

        var found = await store.FindAsync("wfexec-1", "timer-1");
        Assert.NotNull(found);
        Assert.Equal(Now + TimeSpan.FromMinutes(5), found!.DueTime);
        Assert.Equal("DurableTimer", found.StimulusType);
        Assert.Equal("hash-timer-1", found.StimulusHash);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task ListDue_ReturnsOnlyDueTimers_OrderedByDueTimeThenId_CappedByLimit(string provider)
    {
        await using var fixture = CreateStore(provider);
        IDurableTimerStore store = NewStore(fixture);

        await store.SaveAsync(NewTimer("timer-b", dueOffset: TimeSpan.FromMinutes(-2)));
        await store.SaveAsync(NewTimer("timer-a", dueOffset: TimeSpan.FromMinutes(-2)));
        await store.SaveAsync(NewTimer("timer-early", dueOffset: TimeSpan.FromMinutes(-5)));
        await store.SaveAsync(NewTimer("timer-future", dueOffset: TimeSpan.FromMinutes(10)));

        var due = await store.ListDueAsync(Now, limit: 10);

        // timer-future is excluded (not yet due); ties on due time break by ordinal id (a before b).
        Assert.Equal(new[] { "timer-early", "timer-a", "timer-b" }, due.Select(timer => timer.TimerId));

        var limited = await store.ListDueAsync(Now, limit: 2);
        Assert.Equal(new[] { "timer-early", "timer-a" }, limited.Select(timer => timer.TimerId));
    }

    [Fact]
    public async Task ListDue_UsesDeclaredDueTimeRoute()
    {
        await using var fixture = CreateStore("memory");
        var queries = new RecordingBoundedDocumentStore();
        IDurableTimerStore store = new GroundworkDurableTimerStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            queries);

        await store.ListDueAsync(Now, limit: 17);

        var query = Assert.Single(queries.Observed);
        Assert.Equal(ElsaRuntimeStorageManifest.DurableTimerDocumentKind, query.DocumentKind);
        Assert.Equal(ElsaRuntimeStorageManifest.ListDueDurableTimersQuery, query.QueryIdentity);
        var comparison = Assert.Single(Assert.Single(query.Clauses).Comparisons);
        Assert.Equal(ElsaRuntimeStorageManifest.DurableTimerDueTimeField, comparison.Path);
        Assert.Equal(QueryComparisonOperator.LessThanOrEqual, comparison.Operator);
        Assert.Equal(Now, DateTimeOffset.Parse(Assert.Single(comparison.Values)!));
        Assert.Collection(
            query.Order,
            order =>
            {
                Assert.Equal(ElsaRuntimeStorageManifest.DurableTimerDueTimeField, order.Path);
                Assert.Equal(PhysicalSortDirection.Ascending, order.Direction);
            },
            order =>
            {
                Assert.Equal(ElsaRuntimeStorageManifest.DurableTimerIdField, order.Path);
                Assert.Equal(PhysicalSortDirection.Ascending, order.Direction);
            });
        Assert.Equal(17, query.Take);
    }

    [Fact]
    public async Task ClaimDue_UsesBoundedClaimOrderRoute()
    {
        await using var fixture = CreateStore("memory");
        var queries = new RecordingBoundedDocumentStore();
        IDurableTimerStore store = new GroundworkDurableTimerStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            queries);

        await store.ClaimDueAsync(
            new RuntimeDurableTimerClaimRequest(
                "owner-a",
                Now,
                TimeSpan.FromMinutes(1),
                limit: 17));

        var query = Assert.Single(queries.Observed);
        Assert.Equal(ElsaRuntimeStorageManifest.DurableTimerDocumentKind, query.DocumentKind);
        Assert.Equal(ElsaRuntimeStorageManifest.ClaimDueDurableTimersQuery, query.QueryIdentity);
        var comparison = Assert.Single(Assert.Single(query.Clauses).Comparisons);
        Assert.Equal(ElsaRuntimeStorageManifest.DurableTimerClaimOrderKeyField, comparison.Path);
        Assert.Equal(QueryComparisonOperator.LessThanOrEqual, comparison.Operator);
        var order = Assert.Single(query.Order);
        Assert.Equal(ElsaRuntimeStorageManifest.DurableTimerClaimOrderKeyField, order.Path);
        Assert.Equal(PhysicalSortDirection.Ascending, order.Direction);
        Assert.Equal(17, query.Take);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task ClaimDue_ExpiryRenewalReleaseAndCompletionAreFenced(string provider)
    {
        await using var fixture = CreateStore(provider);
        IDurableTimerStore store = NewStore(fixture);
        await store.SaveAsync(NewTimer("timer-1", dueOffset: TimeSpan.FromMinutes(-1)));

        var initial = Assert.Single(await store.ClaimDueAsync(
            new RuntimeDurableTimerClaimRequest("owner-a", Now, TimeSpan.FromMinutes(1), limit: 1)));
        Assert.Empty(await store.ClaimDueAsync(
            new RuntimeDurableTimerClaimRequest("owner-b", Now.AddSeconds(30), TimeSpan.FromMinutes(1), limit: 1)));

        var reclaimed = Assert.Single(await store.ClaimDueAsync(
            new RuntimeDurableTimerClaimRequest("owner-b", Now.AddMinutes(1), TimeSpan.FromMinutes(1), limit: 1)));
        Assert.True(reclaimed.FencingToken > initial.FencingToken);
        Assert.Equal(
            RuntimeDurableTimerClaimTransitionStatus.Stale,
            (await store.CompleteClaimAsync(initial)).Status);

        var renewal = await store.RenewClaimAsync(
            reclaimed,
            Now.AddMinutes(1),
            TimeSpan.FromMinutes(2));
        var renewed = Assert.IsType<RuntimeDurableTimerClaim>(renewal.Claim);
        Assert.True(renewed.Revision > reclaimed.Revision);
        Assert.Equal(
            RuntimeDurableTimerClaimTransitionStatus.Stale,
            (await store.ReleaseClaimAsync(reclaimed, Now.AddMinutes(4))).Status);
        Assert.Equal(
            RuntimeDurableTimerClaimTransitionStatus.Succeeded,
            (await store.ReleaseClaimAsync(renewed, Now.AddMinutes(4))).Status);

        Assert.Empty(await store.ClaimDueAsync(
            new RuntimeDurableTimerClaimRequest("owner-c", Now.AddMinutes(4).AddTicks(-1), TimeSpan.FromMinutes(1), limit: 1)));
        var released = Assert.Single(await store.ClaimDueAsync(
            new RuntimeDurableTimerClaimRequest("owner-c", Now.AddMinutes(4), TimeSpan.FromMinutes(1), limit: 1)));
        Assert.Equal(1, released.FailureCount);
        Assert.Equal(
            RuntimeDurableTimerClaimTransitionStatus.Succeeded,
            (await store.CompleteClaimAsync(released)).Status);
        Assert.Null(await store.FindAsync("wfexec-1", "timer-1"));
    }

    [Fact]
    public async Task ListAsync_UsesDeclaredWorkflowExecutionRoute()
    {
        await using var fixture = CreateStore("memory");
        var queries = new RecordingBoundedDocumentStore();
        IDurableTimerStore store = new GroundworkDurableTimerStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            queries);

        await store.ListAsync("wfexec-1");

        var query = Assert.Single(queries.Observed);
        Assert.Equal(ElsaRuntimeStorageManifest.DurableTimerDocumentKind, query.DocumentKind);
        Assert.Equal(ElsaRuntimeStorageManifest.ListDurableTimersByWorkflowExecutionQuery, query.QueryIdentity);
        var comparison = Assert.Single(Assert.Single(query.Clauses).Comparisons);
        Assert.Equal(ElsaRuntimeStorageManifest.WorkflowExecutionIdField, comparison.Path);
        Assert.Equal(QueryComparisonOperator.Equal, comparison.Operator);
        Assert.Equal("wfexec-1", Assert.Single(comparison.Values));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task ListAsync_ReturnsOnlyWorkflowTimers_OrderedByTimerId(string provider)
    {
        await using var fixture = CreateStore(provider);
        IDurableTimerStore store = NewStore(fixture);

        await store.SaveAsync(NewTimer("timer-b", workflowExecutionId: "wfexec-1", dueOffset: TimeSpan.FromMinutes(-1)));
        await store.SaveAsync(NewTimer("timer-a", workflowExecutionId: "wfexec-1", dueOffset: TimeSpan.FromMinutes(-1)));
        await store.SaveAsync(NewTimer("timer-other", workflowExecutionId: "wfexec-2", dueOffset: TimeSpan.FromMinutes(-1)));

        var timers = await store.ListAsync("wfexec-1");

        Assert.Equal(new[] { "timer-a", "timer-b" }, timers.Select(timer => timer.TimerId));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Save_IsIdempotentUpsert_ExistingWins(string provider)
    {
        await using var fixture = CreateStore(provider);
        IDurableTimerStore store = NewStore(fixture);

        var first = NewTimer("timer-1", dueOffset: TimeSpan.FromMinutes(5), stimulusHash: "hash-original");
        var reArmed = NewTimer("timer-1", dueOffset: TimeSpan.FromMinutes(99), stimulusHash: "hash-changed");

        var saved = await store.SaveAsync(first);
        var deduplicated = await store.SaveAsync(reArmed);

        Assert.Equal("hash-original", saved.StimulusHash);
        Assert.Equal("hash-original", deduplicated.StimulusHash);

        var found = await store.FindAsync("wfexec-1", "timer-1");
        Assert.Equal("hash-original", found!.StimulusHash);
        Assert.Equal(Now + TimeSpan.FromMinutes(5), found.DueTime);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task TimersSurviveRestart_RoundTripThroughNewBridgeInstance(string provider)
    {
        await using var fixture = CreateStore(provider);

        // First "process": save timers and crash (bridge instance discarded, documents remain).
        IDurableTimerStore store = NewStore(fixture);
        await store.SaveAsync(NewTimer("timer-1", dueOffset: TimeSpan.FromMinutes(-1)));
        await store.SaveAsync(NewTimer("timer-2", dueOffset: TimeSpan.FromMinutes(-1)));

        // Second "process": a fresh bridge over the same store sees the full backlog.
        IDurableTimerStore restarted = NewStore(fixture);
        var due = await restarted.ListDueAsync(Now, limit: 10);

        Assert.Equal(new[] { "timer-1", "timer-2" }, due.Select(timer => timer.TimerId));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Delete_RemovesTimer_AndDeletingMissingIsNoOp(string provider)
    {
        await using var fixture = CreateStore(provider);
        IDurableTimerStore store = NewStore(fixture);

        await store.SaveAsync(NewTimer("timer-1", dueOffset: TimeSpan.FromMinutes(-1)));

        await store.DeleteAsync("wfexec-1", "timer-1");
        Assert.Null(await store.FindAsync("wfexec-1", "timer-1"));

        // Deleting an already-absent timer must not throw (at-least-once pump may delete twice).
        await store.DeleteAsync("wfexec-1", "timer-1");
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task CompositeId_IsCollisionFree_AcrossSeparatorBearingIds(string provider)
    {
        await using var fixture = CreateStore(provider);
        IDurableTimerStore store = NewStore(fixture);

        // ("a:b","c") and ("a","b:c") must not collide onto the same document id.
        await store.SaveAsync(NewTimer("c", workflowExecutionId: "a:b", dueOffset: TimeSpan.FromMinutes(-1)));
        await store.SaveAsync(NewTimer("b:c", workflowExecutionId: "a", dueOffset: TimeSpan.FromMinutes(-1)));

        Assert.NotNull(await store.FindAsync("a:b", "c"));
        Assert.NotNull(await store.FindAsync("a", "b:c"));
        Assert.Equal(2, (await store.ListDueAsync(Now, limit: 10)).Count);
    }

    private static IDurableTimerStore NewStore(GroundworkDocumentStoreFixture fixture) =>
        new GroundworkDurableTimerStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

    private static DurableTimer NewTimer(
        string timerId,
        TimeSpan dueOffset,
        string workflowExecutionId = "wfexec-1",
        string? stimulusHash = null) => new(
        TimerId: timerId,
        WorkflowExecutionId: workflowExecutionId,
        StimulusType: "DurableTimer",
        StimulusHash: stimulusHash ?? $"hash-{timerId}",
        DueTime: Now + dueOffset,
        CreatedAt: Now,
        Input: JsonSerializer.SerializeToElement(new { reason = "delay" }),
        Metadata: new Dictionary<string, string> { ["tag"] = "v1" });

    private static GroundworkDocumentStoreFixture CreateStore(string provider) =>
        GroundworkDocumentStoreFixture.Create(provider);

    private sealed class RecordingBoundedDocumentStore : IBoundedDocumentStore
    {
        public List<DocumentQuery> Observed { get; } = [];

        public Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            Observed.Add(query);
            return Task.FromResult(new DocumentQueryResult([], 0));
        }

        public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
