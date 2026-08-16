using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.PhysicalStorage;
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

    [Fact]
    public async Task ListDue_UsesDeclaredNextOccurrenceRoute()
    {
        await using var fixture = CreateStore("memory");
        var queries = new RecordingBoundedDocumentStore();
        IRecurringTriggerScheduleStore store = new GroundworkRecurringTriggerScheduleStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            queries);

        await store.ListDueAsync(Now, limit: 17);

        var query = Assert.Single(queries.Observed);
        Assert.Equal(ElsaRuntimeStorageManifest.RecurringTriggerScheduleDocumentKind, query.DocumentKind);
        Assert.Equal(ElsaRuntimeStorageManifest.ListDueRecurringTriggerSchedulesQuery, query.QueryIdentity);
        Assert.Collection(
            query.Clauses,
            clause =>
            {
                var comparison = Assert.Single(clause.Comparisons);
                Assert.Equal(ElsaRuntimeStorageManifest.RecurringTriggerScheduleIsActiveField, comparison.Path);
                Assert.Equal(QueryComparisonOperator.Equal, comparison.Operator);
                Assert.Equal(bool.TrueString.ToLowerInvariant(), Assert.Single(comparison.Values));
            },
            clause =>
            {
                var comparison = Assert.Single(clause.Comparisons);
                Assert.Equal(ElsaRuntimeStorageManifest.RecurringTriggerScheduleNextOccurrenceField, comparison.Path);
                Assert.Equal(QueryComparisonOperator.LessThanOrEqual, comparison.Operator);
                Assert.Equal(Now, DateTimeOffset.Parse(Assert.Single(comparison.Values)!));
            });
        Assert.Collection(
            query.Order,
            order =>
            {
                Assert.Equal(ElsaRuntimeStorageManifest.RecurringTriggerScheduleIsActiveField, order.Path);
                Assert.Equal(PhysicalSortDirection.Ascending, order.Direction);
            },
            order =>
            {
                Assert.Equal(ElsaRuntimeStorageManifest.RecurringTriggerScheduleNextOccurrenceField, order.Path);
                Assert.Equal(PhysicalSortDirection.Ascending, order.Direction);
            },
            order =>
            {
                Assert.Equal(ElsaRuntimeStorageManifest.RecurringTriggerScheduleIdField, order.Path);
                Assert.Equal(PhysicalSortDirection.Ascending, order.Direction);
            });
        Assert.Equal(17, query.Take);
    }

    [Fact]
    public async Task ListDue_RejectsLimitsAboveTheRuntimeMaximum()
    {
        await using var fixture = CreateStore("memory");
        IRecurringTriggerScheduleStore store = NewStore(fixture);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            store.ListDueAsync(Now, RuntimeStorePageRequest.MaximumLimit + 1).AsTask());
    }

    [Fact]
    public async Task PublicationAndArtifactPagesUseDeclaredOrderedRoutes()
    {
        await using var fixture = CreateStore("memory");
        var queries = new RecordingBoundedDocumentStore();
        var store = new GroundworkRecurringTriggerScheduleStore(
            fixture.DocumentStore,
            GroundworkTestSerialization.Serializer,
            queries);

        await store.ListByPublicationPageAsync(
            new RecurringTriggerSchedulePublicationPageQuery("publication-1", limit: 17, continuationToken: "next"));
        await store.ListByArtifactPageAsync(
            new RecurringTriggerScheduleArtifactPageQuery("artifact-1", limit: 13, continuationToken: "artifact-next"));

        Assert.Collection(
            queries.Observed,
            query =>
            {
                Assert.Equal(ElsaRuntimeStorageManifest.PageRecurringTriggerSchedulesByPublicationQuery, query.QueryIdentity);
                Assert.Equal(17, query.Take);
                Assert.Equal("next", query.Continuation);
                Assert.Equal(ElsaRuntimeStorageManifest.RecurringTriggerScheduleIdField, Assert.Single(query.Order).Path);
            },
            query =>
            {
                Assert.Equal(ElsaRuntimeStorageManifest.PageRecurringTriggerSchedulesByArtifactQuery, query.QueryIdentity);
                Assert.Equal(13, query.Take);
                Assert.Equal("artifact-next", query.Continuation);
                Assert.Equal(ElsaRuntimeStorageManifest.RecurringTriggerScheduleIdField, Assert.Single(query.Order).Path);
            });
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

        await store.PrepareActivationAsync(
            "publication-1",
            [
                NewSchedule("artifact-1", "node-a", TimeSpan.FromMinutes(1), publicationId: "publication-1", slotId: "slot-a"),
                NewSchedule("artifact-1", "node-b", TimeSpan.FromMinutes(2), publicationId: "publication-1", slotId: "slot-a")
            ]);
        await store.PrepareActivationAsync(
            "publication-2",
            [NewSchedule("artifact-1", "node-c", TimeSpan.FromMinutes(3), publicationId: "publication-2", slotId: "slot-b")]);

        var schedules = await store.ListByActivationAsync("publication-1");

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

    [Fact]
    public async Task Save_RejectsACompetingRepublishThatChangesAfterRead()
    {
        await using var fixture = CreateStore("memory");
        IRecurringTriggerScheduleStore seedStore = NewStore(fixture);
        var original = await seedStore.SaveAsync(
            NewSchedule("art", "node", nextOffset: TimeSpan.FromMinutes(1), expression: "PT1M"));
        var interceptingStore = new InterceptingDocumentStore(fixture.DocumentStore)
        {
            OnBeforeSave = async request =>
            {
                Assert.Equal(1, request.ExpectedVersion);
                var competitor = NewSchedule(
                    "art",
                    "node",
                    nextOffset: TimeSpan.FromMinutes(2),
                    expression: "PT2M");
                await seedStore.SaveAsync(competitor);
            }
        };
        IRecurringTriggerScheduleStore store = new GroundworkRecurringTriggerScheduleStore(
            interceptingStore,
            GroundworkTestSerialization.Serializer);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.SaveAsync(original with
            {
                Expression = "PT3M",
                NextOccurrence = Now.AddMinutes(3)
            }));

        Assert.Equal("PT2M", (await seedStore.FindAsync(original.ScheduleId))!.Expression);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task PreparePublication_StagesCreateOnlyProjectionState(string provider)
    {
        await using var fixture = CreateStore(provider);
        var observingStore = new GroundworkFailureInjectingDocumentStore(
            fixture.DocumentStore,
            fixture.BoundedDocumentStore,
            fixture.DocumentStore.Access,
            new GroundworkFailureController());
        IRecurringTriggerScheduleStore store = new GroundworkRecurringTriggerScheduleStore(
            observingStore,
            GroundworkTestSerialization.Serializer,
            observingStore);

        await store.PrepareActivationAsync(
            "publication-1",
            [NewSchedule(
                "artifact-1",
                "node-a",
                TimeSpan.FromMinutes(1),
                publicationId: "publication-1",
                slotId: "slot-a")]);

        var projectionSave = Assert.Single(observingStore.StagedSaves.Where(request =>
            request.DocumentKind == ElsaRuntimeStorageManifest.PublicationProjectionStateDocumentKind));
        Assert.Equal(0, projectionSave.ExpectedVersion);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task ActivatePublication_StagesProjectionStateWithLoadedVersions(string provider)
    {
        await using var fixture = CreateStore(provider);
        var observingStore = new GroundworkFailureInjectingDocumentStore(
            fixture.DocumentStore,
            fixture.BoundedDocumentStore,
            fixture.DocumentStore.Access,
            new GroundworkFailureController());
        IRecurringTriggerScheduleStore store = new GroundworkRecurringTriggerScheduleStore(
            observingStore,
            GroundworkTestSerialization.Serializer,
            observingStore);

        await store.PrepareActivationAsync(
            "publication-1",
            [NewSchedule("artifact-1", "node-a", TimeSpan.FromMinutes(1), publicationId: "publication-1", slotId: "slot-a")]);
        await store.PrepareActivationAsync(
            "publication-2",
            [NewSchedule("artifact-2", "node-b", TimeSpan.FromMinutes(2), publicationId: "publication-2", slotId: "slot-b")]);

        await store.ActivateAsync("publication-1", replacedPublicationId: null);
        var stagedBeforeReplacement = observingStore.StagedSaves.Count;
        await store.ActivateAsync("publication-2", "publication-1");

        var activationProjectionSaves = observingStore.StagedSaves
            .Skip(stagedBeforeReplacement)
            .Where(request =>
                request.DocumentKind == ElsaRuntimeStorageManifest.PublicationProjectionStateDocumentKind &&
                request.ExpectedVersion is not null)
            .ToArray();
        Assert.Equal([1L, 2L], activationProjectionSaves.Select(request => request.ExpectedVersion).Order());
        Assert.Contains(activationProjectionSaves, request => request.Id.Contains("publication-2", StringComparison.Ordinal));
        Assert.Contains(activationProjectionSaves, request => request.Id.Contains("publication-1", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task ActivatePublication_RejectsDelayedReplacementOfInactivePredecessor(string provider)
    {
        await using var fixture = CreateStore(provider);
        IRecurringTriggerScheduleStore store = NewStore(fixture);

        await store.PrepareActivationAsync(
            "publication-old",
            [NewSchedule(
                "artifact-old",
                "node-old",
                TimeSpan.FromMinutes(1),
                publicationId: "publication-old",
                slotId: "slot-a")]);
        await store.PrepareActivationAsync(
            "publication-a",
            [NewSchedule(
                "artifact-a",
                "node-a",
                TimeSpan.FromMinutes(1),
                publicationId: "publication-a",
                slotId: "slot-a")]);
        await store.PrepareActivationAsync(
            "publication-b",
            [NewSchedule(
                "artifact-b",
                "node-b",
                TimeSpan.FromMinutes(1),
                publicationId: "publication-b",
                slotId: "slot-a")]);
        await store.ActivateAsync("publication-old", replacedPublicationId: null);
        await store.ActivateAsync("publication-a", "publication-old");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ActivateAsync("publication-b", "publication-old").AsTask());

        Assert.True(Assert.Single(await store.ListByActivationAsync("publication-a")).IsActive);
        Assert.False(Assert.Single(await store.ListByActivationAsync("publication-b")).IsActive);
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
        ExecutableNodeId: executableNodeId,
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

        #pragma warning disable GW0004 // Required IDocumentStore compatibility members; the portable query surface is retired but still abstract.
        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<DocumentQueryResult> QueryAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.AnyAsync(query, cancellationToken);
        #pragma warning restore GW0004

        public Task<IDocumentUnitOfWork> BeginAsync(DocumentCommitScope scope, CancellationToken cancellationToken = default) =>
            inner.BeginAsync(scope, cancellationToken);
    }

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
