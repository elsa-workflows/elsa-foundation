using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;
using Xunit;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Tests;

public sealed class PublishingGroundworkStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Snapshot_review_rejects_explicit_wrong_tenant_before_store_io()
    {
        await using var persistence = await PublishingV2TestPersistence.CreateAsync("memory");
        var opens = new List<string>();
        var reviews = new GroundworkPublicationSnapshotReviewStore(
            new RecordingSessionSource(persistence.Sessions, [], opens),
            persistence.Access("tenant-a"),
            new PublishingGroundworkDocumentSerializer());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            reviews.TryAddAsync(Review("review-wrong-scope", Now) with { TenantId = "tenant-b" }).AsTask());

        Assert.Empty(opens);
        Assert.Null(await reviews.FindAsync("review-wrong-scope"));
    }

    [Fact]
    public async Task Activity_publication_receipt_find_rejects_wrong_tenant_before_store_io()
    {
        await using var persistence = await PublishingV2TestPersistence.CreateAsync("memory");
        var opens = new List<string>();
        var receipts = new GroundworkActivityPublicationReceiptStore(
            new RecordingSessionSource(persistence.Sessions, [], opens),
            persistence.Access("tenant-a"),
            new PublishingGroundworkDocumentSerializer());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            receipts.FindAsync("tenant-b", "idempotency-key").AsTask());

        Assert.Empty(opens);
    }

    [Fact]
    public async Task EqualityLookupsUseTheirDeclaredBoundedQueryIdentitiesAndPaths()
    {
        await using var persistence = await PublishingV2TestPersistence.CreateAsync("memory");
        var queries = new List<QueryRequest>();
        var indexHints = new List<string>();
        var source = new RecordingSessionSource(persistence.Sessions, queries, indexHints: indexHints);
        var access = persistence.Access();
        var serializer = new PublishingGroundworkDocumentSerializer();
        var slots = new GroundworkPublicationSlotStore(source, access, serializer);
        var publications = new GroundworkPublicationRecordStore(source, access, serializer);
        var intents = new GroundworkPublicationProjectionIntentStore(source, access, serializer);

        await slots.ListByDefinitionAsync("definition-1");
        await slots.TryActivateAsync("definition-1", "default", "publication-1", 0, Now);
        await publications.ListBySlotAsync("definition-1:default");
        await intents.ListByPublicationAsync("publication-1");

        Assert.Collection(
            queries,
            query => AssertQuery(query, PublishingGroundworkStorageManifest.WorkflowDefinitionIdField, "definition-1"),
            query => AssertQuery(query, PublishingGroundworkStorageManifest.ActivePublicationIdField, "publication-1"),
            query => AssertQuery(query, PublishingGroundworkStorageManifest.SlotIdField, "definition-1:default"),
            query => AssertQuery(query, PublishingGroundworkStorageManifest.PublicationIdField, "publication-1"));
        Assert.Equal(
            [
                PublishingGroundworkStorageManifest.SlotByDefinitionIndex,
                PublishingGroundworkStorageManifest.SlotByActivePublicationIndex,
                PublishingGroundworkStorageManifest.RecordBySlotIndex,
                PublishingGroundworkStorageManifest.IntentByPublicationIndex
            ],
            indexHints);
    }

    [Fact]
    public async Task DeleteExpiredUsesItsDeclaredPredicateOrderingAndBound()
    {
        await using var persistence = await PublishingV2TestPersistence.CreateAsync("memory");
        var queries = new List<QueryRequest>();
        var reviews = new GroundworkPublicationSnapshotReviewStore(
            new RecordingSessionSource(persistence.Sessions, queries),
            persistence.Access(),
            new PublishingGroundworkDocumentSerializer());

        await reviews.DeleteExpiredAsync(Now, 17);

        var query = Assert.Single(queries);
        Assert.Equal(PublishingGroundworkStorageManifest.ExpiresAtField, Assert.IsType<Predicate.Range>(query.Where).Column.Name);
        Assert.Equal(
            [PublishingGroundworkStorageManifest.ExpiresAtField, PublishingGroundworkStorageManifest.IdField],
            query.Order.Select(term => term.Column.Name));
        Assert.Equal(17, query.Paging.Limit);
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task StoresEnforceCasAndSurviveAdapterRestart(string provider)
    {
        await using var persistence = await PublishingV2TestPersistence.CreateAsync(provider);
        var stores = Stores.Create(persistence);

        var initial = await stores.Slots.TryActivateAsync("definition-1", "default", "publication-current", 0, Now);
        Assert.True(initial.Succeeded);
        var duplicateAuthority = await stores.Slots.TryActivateAsync("definition-1", "blue", "publication-current", 0, Now);
        Assert.False(duplicateAuthority.Succeeded);
        Assert.Equal("publication_already_active", duplicateAuthority.Failure?.Code);
        var concurrent = await Task.WhenAll(
            stores.Slots.TryActivateAsync("definition-1", "default", "publication-a", 1, Now.AddMinutes(1)).AsTask(),
            stores.Slots.TryActivateAsync("definition-1", "default", "publication-b", 1, Now.AddMinutes(1)).AsTask());
        Assert.Single(concurrent, x => x.Succeeded);
        Assert.Single(concurrent, x => !x.Succeeded && x.Failure?.Code == "slot_revision_conflict");

        var candidate = Publication("publication-record", PublicationStatus.Candidate);
        await stores.Publications.SaveAsync(candidate);
        var transitions = await Task.WhenAll(
            stores.Publications.TryTransitionAsync(candidate with { Status = PublicationStatus.Active, ActivatedAt = Now }, PublicationStatus.Candidate).AsTask(),
            stores.Publications.TryTransitionAsync(candidate with { Status = PublicationStatus.Failed, Failure = new PublicationFailure("lost", "Lost CAS") }, PublicationStatus.Candidate).AsTask());
        Assert.Single(transitions, x => x);

        var policy = new PublicationPolicy("definition-1", PublicationPolicyDefaultAction.ReplaceDefaultSlot, "default", 0, Now);
        var policyWrites = await Task.WhenAll(
            stores.Policies.TrySaveAsync(policy, 0).AsTask(),
            stores.Policies.TrySaveAsync(policy with { DefaultSlotName = "blue" }, 0).AsTask());
        Assert.Single(policyWrites, x => x.Succeeded);

        var intent = new PublicationProjectionIntent(
            "intent-1", "publication-record", PublicationProjectionKinds.TriggerBindings,
            PublicationProjectionOperation.Prepare, PublicationProjectionIntentStatus.Pending, 0, null, null);
        await stores.Intents.SaveAsync(intent);
        var claimed = await stores.Intents.TryTransitionAsync(
            intent with { Status = PublicationProjectionIntentStatus.Delivering, AttemptCount = 1 },
            PublicationProjectionIntentStatus.Pending);
        Assert.True(claimed.Succeeded);

        persistence.Restart();
        stores = Stores.Create(persistence);

        var slot = await stores.Slots.FindAsync("definition-1", "default");
        Assert.Equal(2, slot!.Revision);
        Assert.Contains(slot.ActivePublicationId, new[] { "publication-a", "publication-b" });
        Assert.Single(await stores.Slots.ListByDefinitionAsync("definition-1"));
        Assert.Single(await stores.Publications.ListBySlotAsync(candidate.SlotId));
        Assert.NotEqual(PublicationStatus.Candidate, (await stores.Publications.FindAsync(candidate.PublicationId))!.Status);
        Assert.Equal(1, (await stores.Policies.FindAsync("definition-1"))!.Revision);
        Assert.Equal(PublicationProjectionIntentStatus.Delivering, (await stores.Intents.FindAsync("intent-1"))!.Status);
        Assert.Single(await stores.Intents.ListByPublicationAsync("publication-record"));
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task Concurrent_different_slots_cannot_authorize_the_same_publication(string provider)
    {
        await using var persistence = await PublishingV2TestPersistence.CreateAsync(provider);
        var stores = Stores.Create(persistence);

        var transitions = await Task.WhenAll(
            stores.Slots.TryActivateAsync("definition-1", "red", "publication-shared", 0, Now).AsTask(),
            stores.Slots.TryActivateAsync("definition-1", "blue", "publication-shared", 0, Now).AsTask());

        Assert.Single(transitions, transition => transition.Succeeded);
        var rejected = Assert.Single(transitions, transition => !transition.Succeeded);
        Assert.Equal("publication_already_active", rejected.Failure?.Code);
        Assert.Single(await stores.Slots.ListByDefinitionAsync("definition-1"));
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task SnapshotReviewsAreCrossReplicaSingleUseAndCleanupIsBounded(string provider)
    {
        await using var persistence = await PublishingV2TestPersistence.CreateAsync(provider);
        var firstReplica = ReviewStore(persistence);
        var review = Review("token-1", Now.AddMinutes(15));

        Assert.True(await firstReplica.TryAddAsync(review));
        persistence.Restart();
        firstReplica = ReviewStore(persistence);
        var secondReplica = ReviewStore(persistence);
        Assert.Equal(review, await secondReplica.FindAsync(review.PreflightToken));

        var consumers = await Task.WhenAll(
            firstReplica.TryConsumeAsync(review.PreflightToken).AsTask(),
            secondReplica.TryConsumeAsync(review.PreflightToken).AsTask());
        Assert.Single(consumers, consumed => consumed);

        Assert.True(await firstReplica.TryAddAsync(Review("000-active", Now.AddMinutes(30))));
        Assert.True(await firstReplica.TryAddAsync(Review("expired-newer", Now.AddMinutes(-1))));
        Assert.True(await firstReplica.TryAddAsync(Review("expired-oldest", Now.AddMinutes(-2))));
        Assert.Equal(1, await firstReplica.DeleteExpiredAsync(Now, maxCount: 1));
        Assert.Null(await firstReplica.FindAsync("expired-oldest"));
        Assert.NotNull(await firstReplica.FindAsync("expired-newer"));
        Assert.NotNull(await firstReplica.FindAsync("000-active"));
    }

    [Fact]
    public async Task NativeSqliteListAndDrainShapesTraverseMoreThanOneProviderPage()
    {
        await using var persistence = await PublishingV2TestPersistence.CreateAsync("sqlite");
        var stores = Stores.Create(persistence);
        const int count = 300;

        for (var index = 0; index < count; index++)
        {
            var suffix = index.ToString("D4");
            Assert.True((await stores.Slots.TryActivateAsync(
                "definition-1", $"bulk-{suffix}", $"publication-slot-{suffix}", 0, Now)).Succeeded);
            await stores.Publications.SaveAsync(BulkPublication($"publication-record-{suffix}", "definition-1:bulk"));
            await stores.Intents.SaveAsync(new PublicationProjectionIntent(
                $"intent-{suffix}", "bulk-publication", PublicationProjectionKinds.TriggerBindings,
                PublicationProjectionOperation.Prepare, PublicationProjectionIntentStatus.Pending, 0, null, null));
            Assert.True(await stores.Reviews.TryAddAsync(Review($"expired-{suffix}", Now.AddMinutes(-1))));
            Assert.True((await stores.DraftRuns.TryCreateAsync(BulkDraftTestRun($"draft-{suffix}", Now.AddMinutes(-1)))).Created);
        }

        Assert.Equal(count, (await stores.Slots.ListByDefinitionAsync("definition-1")).Count);
        Assert.Equal(count, (await stores.Publications.ListBySlotAsync("definition-1:bulk")).Count);
        Assert.Equal(count, (await stores.Intents.ListByPublicationAsync("bulk-publication")).Count);
        Assert.Equal(count, await stores.Reviews.DeleteExpiredAsync(Now, count));
        Assert.Equal(count, await stores.DraftRuns.DeleteExpiredAsync(Now, count));
    }

    [Fact]
    public void Active_publication_index_is_sparse_and_unique()
    {
        var unit = PublishingGroundworkStorageManifest.Require(
            PublishingGroundworkStorageManifest.PublicationSlotDocumentKind);
        var index = Assert.Single(unit.Indexes, index =>
            index.Name == PublishingGroundworkStorageManifest.SlotByActivePublicationIndex);

        Assert.True(index.IsUnique);
        Assert.Equal(MissingValueBehavior.Excluded, index.MissingValues);
        Assert.Equal(
            [PublishingGroundworkStorageManifest.ActivePublicationIdField],
            index.Columns.Select(column => column.Column));
    }

    private static GroundworkPublicationSnapshotReviewStore ReviewStore(PublishingV2TestPersistence persistence) =>
        new(persistence.Sessions, persistence.Access("tenant-a"), new PublishingGroundworkDocumentSerializer());

    private static PublicationSnapshotReview Review(string token, DateTimeOffset expiresAt) => new(
        token, "sha256:candidate", "definition-1", PublicationAction.Replace, "default",
        PublicationPolicySource.Workflow, 7, PublicationAction.Replace, "default", "publication-current",
        3, "publication-current", "tenant-a", expiresAt);

    private static PublicationRecord Publication(string id, PublicationStatus status) => new(
        id,
        PublicationSlotIdentity.Create("definition-1", "default"),
        "definition-1",
        "version-1",
        "artifact-1",
        "reference-1",
        0,
        status,
        Now,
        null,
        null,
        null);

    private static PublicationRecord BulkPublication(string id, string slotId) => new(
        id,
        slotId,
        "definition-1",
        "version-1",
        $"artifact-{id}",
        $"reference-{id}",
        0,
        PublicationStatus.Candidate,
        Now,
        null,
        null,
        null,
        "bulk");

    private static void AssertQuery(QueryRequest query, string field, string value)
    {
        var equality = Assert.IsType<Predicate.Equal>(query.Where);
        Assert.Equal(field, equality.Column.Name);
        Assert.Equal(value, equality.Value.Value);
    }

    private sealed record Stores(
        GroundworkPublicationSlotStore Slots,
        GroundworkPublicationRecordStore Publications,
        GroundworkPublicationPolicyStore Policies,
        GroundworkPublicationProjectionIntentStore Intents,
        GroundworkPublicationSnapshotReviewStore Reviews,
        GroundworkActivityDraftTestRunStore DraftRuns)
    {
        public static Stores Create(PublishingV2TestPersistence persistence)
        {
            var serializer = new PublishingGroundworkDocumentSerializer();
            var access = persistence.Access();
            return new(
                new GroundworkPublicationSlotStore(persistence.Sessions, access, serializer),
                new GroundworkPublicationRecordStore(persistence.Sessions, access, serializer),
                new GroundworkPublicationPolicyStore(persistence.Sessions, access, serializer),
                new GroundworkPublicationProjectionIntentStore(persistence.Sessions, access, serializer),
                new GroundworkPublicationSnapshotReviewStore(persistence.Sessions, access, serializer),
                new GroundworkActivityDraftTestRunStore(persistence.Sessions, access, serializer));
        }
    }

    private static ActivityDraftTestRunReceipt BulkDraftTestRun(string draftId, DateTimeOffset expiresAt)
    {
        const string tenantId = "tenant-a";
        const string idempotencyKey = "bulk-key";
        var operationScope = ActivityDraftTestRunIdentity.CreateOperationScope(tenantId);
        return new ActivityDraftTestRunReceipt(
            ActivityDraftTestRunIdentity.CreateTestRunId(operationScope, draftId, idempotencyKey),
            operationScope,
            ActivityDraftTestRunIdentity.HashIdempotencyKey(idempotencyKey),
            draftId,
            1,
            "definition-1",
            tenantId,
            tenantId,
            $"fingerprint-{draftId}",
            ActivityDraftTestRunIdentity.CreateWorkflowExecutionId(operationScope, draftId, idempotencyKey),
            ActivityDraftTestRunReceiptStatus.Preparing,
            null,
            null,
            null,
            null,
            Now,
            Now,
            expiresAt,
            expiresAt,
            ActivityDraftTestRunCancellationStatus.Available,
            1);
    }

    private sealed class RecordingSessionSource(
        IGroundworkStorageSessionSource inner,
        ICollection<QueryRequest> requests,
        ICollection<string>? opens = null,
        ICollection<string>? indexHints = null) : IGroundworkStorageSessionSource
    {
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            opens?.Add(unitId);
            return new RecordingSession(inner.Open(unitId, access, targetName), requests, indexHints);
        }

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) => inner.BeginUnitOfWork(access, options, unitIds, targetName);

        public StorageUnit Unit(string unitId, string? targetName = null) => inner.Unit(unitId, targetName);
    }

    private sealed class RecordingSession(
        IStorageSession inner,
        ICollection<QueryRequest> requests,
        ICollection<string>? indexHints) : IStorageSession, IConcurrencyStorageSession
    {
        public RecordingSession(IStorageSession inner, ICollection<QueryRequest> requests)
            : this(inner, requests, null)
        {
        }

        public StorageUnit Unit => inner.Unit;
        public StorageAccess Access => inner.Access;
        public StoredEntry? Read(StorageKey key) => inner.Read(key);
        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null)
        {
            requests.Add(request);
            if (options?.SelectedIndex is { } selectedIndex)
                indexHints?.Add(selectedIndex);
            return inner.Query(request, options);
        }
        public AggregationResult Aggregate(AggregationQuery query) => inner.Aggregate(query);
        public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => inner.Insert(values, options);
        public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => inner.Update(values, options);
        public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => inner.Upsert(values, options);
        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => inner.Delete(key, options);
        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => inner.Append(operationId, values);

        // The publishing stores write through the concurrency seam, so a session that hides it would
        // change what is under test from "which route did this read take" to "does the double compile".
        public WriteOutcome ConditionalUpsert(StorageValues values, WriteOptions options) =>
            inner is IConcurrencyStorageSession concurrency
                ? concurrency.ConditionalUpsert(values, options)
                : throw new NotSupportedException("The recorded session has no optimistic concurrency.");
    }
}
