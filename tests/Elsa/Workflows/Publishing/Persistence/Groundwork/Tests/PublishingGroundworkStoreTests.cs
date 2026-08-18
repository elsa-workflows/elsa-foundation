using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Capabilities;
using Groundwork.Documents.Scoping;
using Groundwork.Core.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Sqlite.Documents;
using Xunit;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Tests;

public sealed class PublishingGroundworkStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Snapshot_review_rejects_explicit_wrong_tenant_before_store_io()
    {
        var documents = new InMemoryDocumentStore(PublishingGroundworkStorageManifest.Create());
        var reviews = new GroundworkPublicationSnapshotReviewStore(
            documents,
            new PublishingGroundworkDocumentSerializer(),
            GroundworkTestAccess.AccessContext("tenant-a"),
            new PublishingTestBoundedDocumentStore(documents));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            reviews.TryAddAsync(Review("review-wrong-scope", Now) with { TenantId = "tenant-b" }).AsTask());

        Assert.Equal(0, documents.SaveCount);
        Assert.Empty(documents.Snapshot(PublishingGroundworkStorageManifest.SnapshotReviewDocumentKind));
    }

    [Fact]
    public async Task EqualityLookupsUseTheirDeclaredBoundedQueryIdentitiesAndPaths()
    {
        var documents = new InMemoryDocumentStore(PublishingGroundworkStorageManifest.Create());
        var queries = new RecordingBoundedDocumentStore();
        var serializer = new PublishingGroundworkDocumentSerializer();
        var publications = new GroundworkPublicationRecordStore(documents, serializer, queries);

        await publications.ListBySlotAsync("definition-1:default");

        Assert.Collection(
            queries.Observed,
            query => AssertQuery(
                query,
                "publishingPublicationRecord",
                "list-by-slot",
                "slotId",
                "definition-1:default"));
    }

    [Fact]
    public async Task DeleteExpiredUsesItsDeclaredPredicateOrderingAndBound()
    {
        var documents = new InMemoryDocumentStore(PublishingGroundworkStorageManifest.Create());
        var queries = new RecordingBoundedDocumentStore();
        var reviews = new GroundworkPublicationSnapshotReviewStore(
            documents,
            new PublishingGroundworkDocumentSerializer(),
            GroundworkTestAccess.DefaultAccessContextAccessor,
            queries);

        await reviews.DeleteExpiredAsync(Now, 17);

        AssertDeleteExpiredQuery(Assert.Single(queries.Observed), Now, 17);
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task StoresEnforceCasAndSurviveAdapterRestart(string provider)
    {
        await using var fixture = await PublishingStoreFixture.CreateAsync(provider);
        var serializer = new PublishingGroundworkDocumentSerializer();
        var stores = Stores.Create(fixture.Store, fixture.Queries, serializer);

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

        await fixture.RestartAsync();
        stores = Stores.Create(fixture.Store, fixture.Queries, serializer);

        Assert.Single(await stores.Publications.ListBySlotAsync(candidate.SlotId));
        Assert.NotEqual(PublicationStatus.Candidate, (await stores.Publications.FindAsync(candidate.PublicationId))!.Status);
        Assert.Equal(1, (await stores.Policies.FindAsync("definition-1"))!.Revision);
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task SnapshotReviewsAreCrossReplicaSingleUseAndCleanupIsBounded(string provider)
    {
        await using var fixture = await PublishingStoreFixture.CreateAsync(provider);
        var serializer = new PublishingGroundworkDocumentSerializer();
        var firstReplica = ReviewStore(fixture.Store, fixture.Queries, serializer);
        var review = Review("token-1", Now.AddMinutes(15));

        Assert.True(await firstReplica.TryAddAsync(review));
        await fixture.RestartAsync();
        firstReplica = ReviewStore(fixture.Store, fixture.Queries, serializer);
        var secondReplica = ReviewStore(fixture.Store, fixture.Queries, serializer);
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

    private static GroundworkPublicationSnapshotReviewStore ReviewStore(
        IDocumentStore store,
        IBoundedDocumentStore queries,
        PublishingGroundworkDocumentSerializer serializer) =>
        new(
            store,
            serializer,
            GroundworkTestAccess.AccessContext("tenant-a"),
            queries);

    private static PublicationSnapshotReview Review(string token, DateTimeOffset expiresAt) => new(
        token, "sha256:candidate", "definition-1", PublicationAction.Replace, "default",
        PublicationPolicySource.Workflow, 7, PublicationAction.Replace, "default", "publication-current",
        3, "publication-current", "tenant-a", expiresAt);

    private static PublicationRecord Publication(string id, PublicationStatus status) => new(
        id,
        WorkflowActivationSlotIdentity.Create("definition-1", "default"),
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

    private static void AssertQuery(
        DocumentQuery query,
        string documentKind,
        string queryIdentity,
        string path,
        string value)
    {
        Assert.Equal(documentKind, query.DocumentKind);
        Assert.Equal(queryIdentity, query.QueryIdentity);
        var comparison = Assert.Single(Assert.Single(query.Clauses).Comparisons);
        Assert.Equal(path, comparison.Path);
        Assert.Equal(QueryComparisonOperator.Equal, comparison.Operator);
        Assert.Equal(value, Assert.Single(comparison.Values));
    }

    private static void AssertDeleteExpiredQuery(DocumentQuery query, DateTimeOffset expiresAtOrBefore, int maxCount)
    {
        Assert.Equal(PublishingGroundworkStorageManifest.SnapshotReviewDocumentKind, query.DocumentKind);
        Assert.Equal(PublishingGroundworkStorageManifest.DeleteExpiredQuery, query.QueryIdentity);
        var comparison = Assert.Single(Assert.Single(query.Clauses).Comparisons);
        Assert.Equal(PublishingGroundworkStorageManifest.ExpiresAtField, comparison.Path);
        Assert.Equal(QueryComparisonOperator.LessThanOrEqual, comparison.Operator);
        Assert.Equal(expiresAtOrBefore, DateTimeOffset.Parse(Assert.Single(comparison.Values)!));
        var order = Assert.Single(query.Order);
        Assert.Equal(PublishingGroundworkStorageManifest.ExpiresAtField, order.Path);
        Assert.Equal(global::Groundwork.Core.PhysicalStorage.PhysicalSortDirection.Ascending, order.Direction);
        Assert.Equal(maxCount, query.Take);
    }

    private sealed record Stores(
        GroundworkPublicationRecordStore Publications,
        GroundworkPublicationPolicyStore Policies)
    {
        public static Stores Create(
            IDocumentStore store,
            IBoundedDocumentStore queries,
            PublishingGroundworkDocumentSerializer serializer)
        {
            return new Stores(
                new GroundworkPublicationRecordStore(store, serializer, queries),
                new GroundworkPublicationPolicyStore(store, serializer));
        }
    }

    private sealed class PublishingStoreFixture(
        string provider,
        string? sqlitePath,
        IDocumentStore store,
        IBoundedDocumentStore queries) : IAsyncDisposable
    {
        private static readonly ProviderIdentity SqliteProvider = new("publishing-groundwork-sqlite-tests", "1.0.0");
        public IDocumentStore Store { get; private set; } = store;

        /// <summary>
        /// The bounded surface paired with <see cref="Store"/>: the in-memory double's evaluator for the memory
        /// lane, and the provider's own route-bound query runtime for the SQLite lane.
        /// </summary>
        public IBoundedDocumentStore Queries { get; private set; } = queries;

        public static async Task<PublishingStoreFixture> CreateAsync(string provider)
        {
            if (provider == "memory")
            {
                var documents = new InMemoryDocumentStore(PublishingGroundworkStorageManifest.Create());
                return new PublishingStoreFixture(
                    provider,
                    null,
                    documents,
                    new PublishingTestBoundedDocumentStore(documents));
            }

            var path = Path.Combine(Path.GetTempPath(), $"elsa-publishing-{Guid.NewGuid():N}.db");
            var fixture = new PublishingStoreFixture(provider, path, null!, null!);
            await fixture.OpenSqliteAsync();
            return fixture;
        }

        public async Task RestartAsync()
        {
            if (provider == "memory")
                return;
            await OpenSqliteAsync();
        }

        private async Task OpenSqliteAsync()
        {
            var manifest = PublishingGroundworkStorageManifest.Create();
            // Pooling=False so disposing the store releases the OS file handle immediately; otherwise a pooled
            // SQLite connection keeps the temp .db open and the cleanup File.Delete throws IOException on Windows.
            var opened = await GroundworkPhysicalTestStores.OpenSqliteAsync(
                $"Data Source={sqlitePath};Pooling=False",
                manifest,
                SqliteProvider,
                DocumentStoreAccess.Scoped(new StorageScope("default")));
            Store = opened.Store;
            Queries = opened.Queries;
        }

        public ValueTask DisposeAsync()
        {
            if (sqlitePath is not null && File.Exists(sqlitePath))
                File.Delete(sqlitePath);
            return ValueTask.CompletedTask;
        }
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
