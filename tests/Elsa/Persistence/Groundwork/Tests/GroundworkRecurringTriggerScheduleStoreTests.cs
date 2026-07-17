using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

/// <summary>
/// Contract coverage for the durable <see cref="IRecurringTriggerScheduleStore"/> Groundwork bridge (W16). The
/// same assertions run against both a real Groundwork SQLite provider and an in-memory document store, proving
/// the recurring-start bridge is provider-neutral like its durable-timer sibling.
/// </summary>
public sealed class GroundworkRecurringTriggerScheduleStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Save_Find_RoundTripsSchedule(string provider)
    {
        await using var fixture = CreateStore(provider);
        IRecurringTriggerScheduleStore store = NewStore(fixture);

        var saved = await store.SaveAsync(NewSchedule("artifact-1", "node-1", nextOffset: TimeSpan.FromMinutes(5)));
        Assert.Equal(RecurringTriggerSchedule.BuildId("artifact-1", "node-1"), saved.ScheduleId);

        var found = await store.FindAsync(saved.ScheduleId);
        Assert.NotNull(found);
        Assert.Equal("Timer", found!.StimulusType);
        Assert.Equal(RecurringScheduleKind.Interval, found.Kind);
        Assert.Equal("PT5M", found.Expression);
        Assert.Equal(Now + TimeSpan.FromMinutes(5), found.NextOccurrence);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task ListDue_ReturnsOnlyDue_OrderedByNextThenId_CappedByLimit(string provider)
    {
        await using var fixture = CreateStore(provider);
        IRecurringTriggerScheduleStore store = NewStore(fixture);

        await store.SaveAsync(NewSchedule("art", "b", nextOffset: TimeSpan.FromMinutes(-2)));
        await store.SaveAsync(NewSchedule("art", "a", nextOffset: TimeSpan.FromMinutes(-2)));
        await store.SaveAsync(NewSchedule("art", "early", nextOffset: TimeSpan.FromMinutes(-5)));
        await store.SaveAsync(NewSchedule("art", "future", nextOffset: TimeSpan.FromMinutes(10)));

        var due = await store.ListDueAsync(Now, limit: 10);
        Assert.Equal(
            new[]
            {
                RecurringTriggerSchedule.BuildId("art", "early"),
                RecurringTriggerSchedule.BuildId("art", "a"),
                RecurringTriggerSchedule.BuildId("art", "b")
            },
            due.Select(s => s.ScheduleId));

        var limited = await store.ListDueAsync(Now, limit: 2);
        Assert.Equal(
            new[]
            {
                RecurringTriggerSchedule.BuildId("art", "early"),
                RecurringTriggerSchedule.BuildId("art", "a")
            },
            limited.Select(s => s.ScheduleId));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Save_IsUpsert_RepublishReplaces(string provider)
    {
        await using var fixture = CreateStore(provider);
        IRecurringTriggerScheduleStore store = NewStore(fixture);

        await store.SaveAsync(NewSchedule("art", "node", nextOffset: TimeSpan.FromMinutes(5), expression: "PT5M"));
        await store.SaveAsync(NewSchedule("art", "node", nextOffset: TimeSpan.FromMinutes(9), expression: "PT9M"));

        // Unlike the durable-timer existing-wins rule, a recurring schedule is rewritten on republish.
        var found = await store.FindAsync(RecurringTriggerSchedule.BuildId("art", "node"));
        Assert.Equal("PT9M", found!.Expression);
        Assert.Equal(Now + TimeSpan.FromMinutes(9), found.NextOccurrence);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task ListByPublication_UsesPublicationScopedRoute(string provider)
    {
        await using var fixture = CreateStore(provider);
        IRecurringTriggerScheduleStore store = NewStore(fixture);

        await store.PreparePublicationAsync(
            "publication-1",
            [
                NewSchedule("artifact-1", "node-a", TimeSpan.FromMinutes(1), publicationId: "publication-1", slotId: "slot-a"),
                NewSchedule("artifact-1", "node-b", TimeSpan.FromMinutes(2), publicationId: "publication-1", slotId: "slot-a")
            ]);
        await store.PreparePublicationAsync(
            "publication-2",
            [NewSchedule("artifact-1", "node-c", TimeSpan.FromMinutes(3), publicationId: "publication-2", slotId: "slot-b")]);

        var schedules = await store.ListByPublicationAsync("publication-1");

        Assert.Equal(
            new[]
            {
                RecurringTriggerSchedule.BuildId("publication-1", "artifact-1", "node-a"),
                RecurringTriggerSchedule.BuildId("publication-1", "artifact-1", "node-b")
            },
            schedules.Select(schedule => schedule.ScheduleId).OrderBy(x => x));
        Assert.All(schedules, schedule =>
        {
            Assert.Equal("publication-1", schedule.PublicationId);
            Assert.False(schedule.IsActive);
        });
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task TryAdvance_ClaimsOccurrence_OnlyWhenCursorMatches(string provider)
    {
        await using var fixture = CreateStore(provider);
        IRecurringTriggerScheduleStore store = NewStore(fixture);

        var schedule = await store.SaveAsync(NewSchedule("art", "node", nextOffset: TimeSpan.FromMinutes(-1)));
        var expected = schedule.NextOccurrence;
        var advanced = expected + TimeSpan.FromMinutes(5);

        // Stale expectation loses the CAS.
        Assert.False(await store.TryAdvanceAsync(schedule.ScheduleId, expected + TimeSpan.FromSeconds(1), advanced));

        // Correct expectation wins and moves the cursor.
        Assert.True(await store.TryAdvanceAsync(schedule.ScheduleId, expected, advanced));
        Assert.Equal(advanced, (await store.FindAsync(schedule.ScheduleId))!.NextOccurrence);

        // Re-claiming the already-advanced occurrence loses (at-most-once).
        Assert.False(await store.TryAdvanceAsync(schedule.ScheduleId, expected, advanced));
    }

    [Fact]
    public async Task TryAdvance_ReturnsFalse_WhenProviderVersionChangesBetweenReadAndWrite()
    {
        await using var fixture = CreateStore("memory");
        IRecurringTriggerScheduleStore seedStore = NewStore(fixture);
        var schedule = await seedStore.SaveAsync(NewSchedule("art", "node", nextOffset: TimeSpan.FromMinutes(-1)));
        var expected = schedule.NextOccurrence;
        var advanced = expected + TimeSpan.FromMinutes(5);
        var interceptingStore = new InterceptingDocumentStore(fixture.DocumentStore)
        {
            OnBeforeSave = async request =>
            {
                Assert.Equal(1, request.ExpectedVersion);
                var competingWrite = await fixture.DocumentStore.SaveAsync(new SaveDocumentRequest(
                    request.DocumentKind,
                    request.Id,
                    request.SchemaVersion,
                    request.ContentJson));
                Assert.Equal(DocumentStoreWriteStatus.Saved, competingWrite.Status);
            }
        };
        IRecurringTriggerScheduleStore store = new GroundworkRecurringTriggerScheduleStore(
            interceptingStore,
            GroundworkTestSerialization.Serializer);

        Assert.False(await store.TryAdvanceAsync(schedule.ScheduleId, expected, advanced));

        var found = await seedStore.FindAsync(schedule.ScheduleId);
        Assert.Equal(advanced, found!.NextOccurrence);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task DeleteByArtifact_RemovesOnlyThatArtifactsSchedules(string provider)
    {
        await using var fixture = CreateStore(provider);
        IRecurringTriggerScheduleStore store = NewStore(fixture);

        await store.SaveAsync(NewSchedule("art-1", "a", nextOffset: TimeSpan.FromMinutes(-1)));
        await store.SaveAsync(NewSchedule("art-1", "b", nextOffset: TimeSpan.FromMinutes(-1)));
        await store.SaveAsync(NewSchedule("art-2", "c", nextOffset: TimeSpan.FromMinutes(-1)));

        await store.DeleteByArtifactAsync("art-1");

        var remaining = await store.ListDueAsync(Now, limit: 10);
        Assert.Equal(new[] { RecurringTriggerSchedule.BuildId("art-2", "c") }, remaining.Select(s => s.ScheduleId));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Delete_RemovesSchedule_AndDeletingMissingIsNoOp(string provider)
    {
        await using var fixture = CreateStore(provider);
        IRecurringTriggerScheduleStore store = NewStore(fixture);

        var schedule = await store.SaveAsync(NewSchedule("art", "node", nextOffset: TimeSpan.FromMinutes(-1)));

        await store.DeleteAsync(schedule.ScheduleId);
        Assert.Null(await store.FindAsync(schedule.ScheduleId));

        // Deleting an already-absent schedule must not throw.
        await store.DeleteAsync(schedule.ScheduleId);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task SchedulesSurviveRestart_RoundTripThroughNewBridgeInstance(string provider)
    {
        await using var fixture = CreateStore(provider);

        IRecurringTriggerScheduleStore store = NewStore(fixture);
        await store.SaveAsync(NewSchedule("art", "a", nextOffset: TimeSpan.FromMinutes(-1)));
        await store.SaveAsync(NewSchedule("art", "b", nextOffset: TimeSpan.FromMinutes(-1)));

        IRecurringTriggerScheduleStore restarted = NewStore(fixture);
        var due = await restarted.ListDueAsync(Now, limit: 10);

        Assert.Equal(
            new[] { RecurringTriggerSchedule.BuildId("art", "a"), RecurringTriggerSchedule.BuildId("art", "b") },
            due.Select(s => s.ScheduleId));
    }

    private static IRecurringTriggerScheduleStore NewStore(GroundworkDocumentStoreFixture fixture) =>
        new GroundworkRecurringTriggerScheduleStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

    private static RecurringTriggerSchedule NewSchedule(
        string artifactId,
        string executableNodeId,
        TimeSpan nextOffset,
        string expression = "PT5M",
        string? publicationId = null,
        string? slotId = null) => new(
        ScheduleId: publicationId is null
            ? RecurringTriggerSchedule.BuildId(artifactId, executableNodeId)
            : RecurringTriggerSchedule.BuildId(publicationId, artifactId, executableNodeId),
        ArtifactId: artifactId,
        StimulusType: "Timer",
        StimulusHash: $"sha256:{executableNodeId}",
        Kind: RecurringScheduleKind.Interval,
        Expression: expression,
        NextOccurrence: Now + nextOffset,
        CreatedAt: Now,
        PublicationId: publicationId,
        SlotId: slotId);

    private static GroundworkDocumentStoreFixture CreateStore(string provider) =>
        GroundworkDocumentStoreFixture.Create(provider);

    private sealed class InterceptingDocumentStore(IDocumentStore inner) : IDocumentStore
    {
        public Func<SaveDocumentRequest, Task>? OnBeforeSave { get; set; }
        public DocumentStoreAccess Access => inner.Access;
        public TransactionBoundary TransactionBoundary => inner.TransactionBoundary;

        public async Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default)
        {
            if (OnBeforeSave is { } hook)
            {
                OnBeforeSave = null;
                await hook(request);
            }

            return await inner.SaveAsync(request, cancellationToken);
        }

        public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default) =>
            inner.LoadAsync(documentKind, id, cancellationToken);

        public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(request, cancellationToken);

        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<DocumentQueryResult> QueryAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.AnyAsync(query, cancellationToken);

        public Task<IDocumentUnitOfWork> BeginAsync(DocumentCommitScope scope, CancellationToken cancellationToken = default) =>
            inner.BeginAsync(scope, cancellationToken);
    }
}
