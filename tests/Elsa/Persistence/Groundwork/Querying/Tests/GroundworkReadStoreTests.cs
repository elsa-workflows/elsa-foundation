using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Persistence.Core.Queries;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Primitives.Entities;
using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Core.Scoping;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Xunit;

namespace Elsa.Persistence.Groundwork.Querying.Tests;

/// <summary>
/// Proves <see cref="GroundworkReadStore{TEntity}"/> satisfies the full closed <c>Query&lt;TEntity&gt;</c>
/// contract over a Groundwork document provider whose only native query is equality-on-index — the exact
/// surface the in-memory double reproduces from the manifest. These are the same design-lane query shapes
/// the EF Core translator proves, executed here against documents, so a host that selects a Groundwork
/// (document) provider gets the same result set as the relational provider. This is the concrete evidence
/// that design persistence can run on either a relational or a document database, host's choice.
/// </summary>
public class GroundworkReadStoreTests
{
    private const string DocumentKind = "doc";
    private const string CollectionIndex = "by-collection";
    private const string CollectionField = "collection";
    private const string ListAllQuery = "list-all";
    private const string CollectionValue = "doc";
    private const string SchemaVersion = "1.0.0";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyList<DocumentQueryOrder> CollectionOrder =
        [new(CollectionField)];
    private static readonly IReadOnlyList<DocumentQueryOrder> DocumentIdentityOrder =
        [new(PhysicalDocumentFieldPaths.Id)];

    private sealed class Doc : Entity
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Category { get; set; } = string.Empty;
        public string SortKey { get; set; } = string.Empty;
    }

    private static async Task<GroundworkReadStore<Doc>> SeededStoreAsync(params Doc[] docs)
    {
        var store = new InMemoryDocumentStore(BuildManifest());

        foreach (var doc in docs)
        {
            var content = JsonSerializer.Serialize(new GroundworkDocument<Doc>(CollectionValue, doc), Json);
            await store.SaveAsync(new SaveDocumentRequest(DocumentKind, doc.Id, SchemaVersion, content));
        }

        return new GroundworkReadStore<Doc>(
            store,
            DocumentKind,
            ListAllQuery,
            CollectionField,
            CollectionValue,
            Json,
            collectionOrder: CollectionOrder);
    }

    private static Doc[] Sample() =>
    [
        new Doc { Id = "a", Name = "Order Processing", Description = "Handles orders", Category = "sales", SortKey = "0000000003" },
        new Doc { Id = "b", Name = "invoice generator", Description = null, Category = "finance", SortKey = "0000000001" },
        new Doc { Id = "c", Name = "Shipping", Description = "ORDER fulfilment", Category = "ops", SortKey = "0000000002" },
    ];

    [Fact]
    public async Task FindById_uses_point_read_fast_path()
    {
        var store = await SeededStoreAsync(Sample());
        var result = await store.FirstOrDefaultAsync(Query<Doc>.Where(x => x.Id, QueryOp.Equal, "b"));
        Assert.NotNull(result);
        Assert.Equal("invoice generator", result!.Name);
    }

    [Fact]
    public async Task FindById_returns_null_when_absent()
    {
        var store = await SeededStoreAsync(Sample());
        var result = await store.FirstOrDefaultAsync(Query<Doc>.Where(x => x.Id, QueryOp.Equal, "missing"));
        Assert.Null(result);
    }

    [Fact]
    public async Task FindById_does_not_require_a_bounded_query_runtime()
    {
        var inner = new InMemoryDocumentStore(BuildManifest());
        var doc = Sample()[0];
        var content = JsonSerializer.Serialize(new GroundworkDocument<Doc>(CollectionValue, doc), Json);
        await inner.SaveAsync(new SaveDocumentRequest(DocumentKind, doc.Id, SchemaVersion, content));
        var store = new GroundworkReadStore<Doc>(
            new DocumentStoreOnlyAdapter(inner),
            DocumentKind,
            ListAllQuery,
            CollectionField,
            CollectionValue,
            Json);

        var result = await store.FirstOrDefaultAsync(Query<Doc>.Where(x => x.Id, QueryOp.Equal, doc.Id));

        Assert.Equal(doc.Name, result?.Name);
    }

    [Fact]
    public async Task Collection_query_requires_a_bounded_runtime_when_executed()
    {
        var store = new GroundworkReadStore<Doc>(
            new DocumentStoreOnlyAdapter(new InMemoryDocumentStore(BuildManifest())),
            DocumentKind,
            ListAllQuery,
            CollectionField,
            CollectionValue,
            Json);

        var exception = await Assert.ThrowsAsync<GroundworkQueryReadinessException>(() => store.QueryAsync(Query<Doc>.All()));

        Assert.Contains(DocumentKind, exception.Message, StringComparison.Ordinal);
        Assert.Contains("bounded", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(DocumentKind, exception.DocumentKind);
        Assert.Equal(ListAllQuery, exception.QueryIdentity);
        Assert.Equal(typeof(Doc), exception.EntityType);
    }

    [Fact]
    public async Task Malformed_document_reports_kind_and_id()
    {
        var documentStore = new InMemoryDocumentStore(BuildManifest());
        await documentStore.SaveAsync(new SaveDocumentRequest(DocumentKind, "bad", SchemaVersion, "null"));
        var store = new GroundworkReadStore<Doc>(documentStore, DocumentKind, ListAllQuery, CollectionField, CollectionValue, Json);

        var exception = await Assert.ThrowsAsync<GroundworkCorruptPayloadException>(() =>
            store.FirstOrDefaultAsync(Query<Doc>.Where(x => x.Id, QueryOp.Equal, "bad")));

        Assert.Contains(DocumentKind, exception.Message);
        Assert.Contains("bad", exception.Message);
        Assert.Equal(DocumentKind, exception.DocumentKind);
        Assert.Equal("bad", exception.DocumentId);
        Assert.Equal(typeof(Doc), exception.EntityType);
    }

    [Fact]
    public async Task Invalid_json_payload_preserves_the_deserialization_failure()
    {
        var documentStore = new InMemoryDocumentStore(BuildManifest());
        await documentStore.SaveAsync(new SaveDocumentRequest(DocumentKind, "invalid-json", SchemaVersion, "{"));
        var store = new GroundworkReadStore<Doc>(documentStore, DocumentKind, ListAllQuery, CollectionField, CollectionValue, Json);

        var exception = await Assert.ThrowsAsync<GroundworkCorruptPayloadException>(() =>
            store.FirstOrDefaultAsync(Query<Doc>.Where(x => x.Id, QueryOp.Equal, "invalid-json")));

        Assert.Equal(DocumentKind, exception.DocumentKind);
        Assert.Equal("invalid-json", exception.DocumentId);
        Assert.Equal(typeof(Doc), exception.EntityType);
        Assert.IsType<JsonException>(exception.InnerException);
    }

    [Fact]
    public async Task Unsupported_payload_materialization_preserves_the_deserialization_failure()
    {
        var documentStore = new InMemoryDocumentStore(BuildManifest());
        var content = JsonSerializer.Serialize(
            new GroundworkDocument<Doc>(CollectionValue, new Doc { Id = "unsupported" }),
            Json);
        await documentStore.SaveAsync(new SaveDocumentRequest(
            DocumentKind,
            "unsupported",
            SchemaVersion,
            content));
        var options = new JsonSerializerOptions(Json);
        options.Converters.Add(new UnsupportedDocConverter());
        var store = new GroundworkReadStore<Doc>(
            documentStore,
            DocumentKind,
            ListAllQuery,
            CollectionField,
            CollectionValue,
            options);

        var exception = await Assert.ThrowsAsync<GroundworkCorruptPayloadException>(() =>
            store.FirstOrDefaultAsync(Query<Doc>.Where(x => x.Id, QueryOp.Equal, "unsupported")));

        Assert.Equal(DocumentKind, exception.DocumentKind);
        Assert.Equal("unsupported", exception.DocumentId);
        Assert.Equal(typeof(Doc), exception.EntityType);
        Assert.IsType<NotSupportedException>(exception.InnerException);
    }

    [Fact]
    public async Task Tenant_agnostic_query_requires_an_authorized_session_factory()
    {
        var store = new GroundworkReadStore<Doc>(
            new InMemoryDocumentStore(BuildManifest()),
            DocumentKind,
            ListAllQuery,
            CollectionField,
            CollectionValue,
            Json);

        var exception = await Assert.ThrowsAsync<GroundworkQueryReadinessException>(() =>
            store.QueryAsync(Query<Doc>.All().IgnoreTenant()));

        Assert.Equal(DocumentKind, exception.DocumentKind);
        Assert.Equal(ListAllQuery, exception.QueryIdentity);
        Assert.Equal(typeof(Doc), exception.EntityType);
    }

    [Fact]
    public async Task Point_read_provider_failure_preserves_context_and_inner_exception()
    {
        var providerFailure = new IOException("point-read-failed");
        var store = new GroundworkReadStore<Doc>(
            new ThrowingDocumentStore(BuildManifest(), providerFailure),
            DocumentKind,
            ListAllQuery,
            CollectionField,
            CollectionValue,
            Json);

        var exception = await Assert.ThrowsAsync<GroundworkProviderFailureException>(() =>
            store.FirstOrDefaultAsync(Query<Doc>.Where(x => x.Id, QueryOp.Equal, "point-id")));

        Assert.Equal(DocumentKind, exception.DocumentKind);
        Assert.Equal("point-id", exception.DocumentId);
        Assert.Equal(typeof(Doc), exception.EntityType);
        Assert.Same(providerFailure, exception.InnerException);
    }

    [Fact]
    public async Task Bounded_query_provider_failure_preserves_context_and_inner_exception()
    {
        var providerFailure = new IOException("bounded-read-failed");
        var store = new GroundworkReadStore<Doc>(
            new ThrowingDocumentStore(BuildManifest(), providerFailure),
            DocumentKind,
            ListAllQuery,
            CollectionField,
            CollectionValue,
            Json,
            new ThrowingBoundedDocumentStore(providerFailure),
            collectionOrder: CollectionOrder);

        var exception = await Assert.ThrowsAsync<GroundworkProviderFailureException>(() =>
            store.QueryAsync(Query<Doc>.All()));

        Assert.Equal(DocumentKind, exception.DocumentKind);
        Assert.Equal(ListAllQuery, exception.QueryIdentity);
        Assert.Equal(typeof(Doc), exception.EntityType);
        Assert.Same(providerFailure, exception.InnerException);
    }

    [Fact]
    public async Task Cancellation_is_not_wrapped_by_the_query_boundary()
    {
        var cancellation = new OperationCanceledException("cancelled");
        var store = new GroundworkReadStore<Doc>(
            new ThrowingDocumentStore(BuildManifest(), cancellation),
            DocumentKind,
            ListAllQuery,
            CollectionField,
            CollectionValue,
            Json);

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            store.FirstOrDefaultAsync(Query<Doc>.Where(x => x.Id, QueryOp.Equal, "point-id")));

        Assert.Same(cancellation, exception);
    }

    [Fact]
    public async Task Bounded_query_cancellation_is_not_wrapped_by_the_query_boundary()
    {
        var cancellation = new OperationCanceledException("cancelled");
        var store = new GroundworkReadStore<Doc>(
            new ThrowingDocumentStore(BuildManifest(), cancellation),
            DocumentKind,
            ListAllQuery,
            CollectionField,
            CollectionValue,
            Json,
            new ThrowingBoundedDocumentStore(cancellation),
            collectionOrder: CollectionOrder);

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            store.QueryAsync(Query<Doc>.All()));

        Assert.Same(cancellation, exception);
    }

    [Fact]
    public async Task Existing_groundwork_query_exceptions_are_not_double_wrapped()
    {
        var existing = new GroundworkQueryReadinessException(
            "already-classified",
            DocumentKind,
            ListAllQuery,
            null,
            typeof(Doc));
        var store = new GroundworkReadStore<Doc>(
            new ThrowingDocumentStore(BuildManifest(), existing),
            DocumentKind,
            ListAllQuery,
            CollectionField,
            CollectionValue,
            Json);

        var exception = await Assert.ThrowsAsync<GroundworkQueryReadinessException>(() =>
            store.FirstOrDefaultAsync(Query<Doc>.Where(x => x.Id, QueryOp.Equal, "point-id")));

        Assert.Same(existing, exception);
    }

    [Fact]
    public async Task Existing_groundwork_query_exceptions_from_bounded_queries_are_not_double_wrapped()
    {
        var existing = new GroundworkQueryReadinessException(
            "already-classified",
            DocumentKind,
            ListAllQuery,
            null,
            typeof(Doc));
        var store = new GroundworkReadStore<Doc>(
            new ThrowingDocumentStore(BuildManifest(), existing),
            DocumentKind,
            ListAllQuery,
            CollectionField,
            CollectionValue,
            Json,
            new ThrowingBoundedDocumentStore(existing),
            collectionOrder: CollectionOrder);

        var exception = await Assert.ThrowsAsync<GroundworkQueryReadinessException>(() =>
            store.QueryAsync(Query<Doc>.All()));

        Assert.Same(existing, exception);
    }

    [Fact]
    public async Task Equal_matches_exact_field()
    {
        var store = await SeededStoreAsync(Sample());
        var result = await store.QueryAsync(Query<Doc>.Where(x => x.Category, QueryOp.Equal, "finance"));
        Assert.Equal(["b"], result.Select(x => x.Id));
    }

    [Fact]
    public async Task In_matches_set_membership()
    {
        var store = await SeededStoreAsync(Sample());
        var result = await store.QueryAsync(Query<Doc>.Where(x => x.Id, QueryOp.In, new[] { "a", "c" }));
        Assert.Equal(["a", "c"], result.Select(x => x.Id).OrderBy(x => x));
    }

    [Fact]
    public async Task Contains_is_case_insensitive_substring()
    {
        var store = await SeededStoreAsync(Sample());
        var result = await store.QueryAsync(Query<Doc>.Where(x => x.Name, QueryOp.Contains, "ORDER"));
        Assert.Equal(["a"], result.Select(x => x.Id));
    }

    [Fact]
    public async Task Or_fans_out_within_a_clause_like_search_term()
    {
        var store = await SeededStoreAsync(Sample());
        const string term = "order";
        var q = Query<Doc>.Where(x => x.Name, QueryOp.Contains, term)
                          .Or(x => x.Description, QueryOp.Contains, term)
                          .Or(x => x.Id, QueryOp.Contains, term);

        var result = await store.QueryAsync(q);

        Assert.Equal(["a", "c"], result.Select(x => x.Id).OrderBy(x => x));
    }

    [Fact]
    public async Task And_across_clauses_intersects()
    {
        var store = await SeededStoreAsync(Sample());
        var q = Query<Doc>.Where(x => x.Name, QueryOp.Contains, "ship")
                          .And(x => x.Category, QueryOp.Equal, "ops");
        var result = await store.QueryAsync(q);
        Assert.Equal(["c"], result.Select(x => x.Id));
    }

    [Fact]
    public async Task OrderByDescending_resolves_latest_version()
    {
        var store = await SeededStoreAsync(Sample());
        // Mirrors FindLatestVersion: order by SemVerSortKey desc, take first.
        var result = await store.FirstOrDefaultAsync(Query<Doc>.All().OrderByDescending(x => x.SortKey));
        Assert.Equal("a", result!.Id);
    }

    [Fact]
    public async Task Any_is_true_when_a_match_exists()
    {
        var store = await SeededStoreAsync(Sample());
        var exists = await store.AnyAsync(
            Query<Doc>.Where(x => x.Category, QueryOp.Equal, "ops").And(x => x.SortKey, QueryOp.Equal, "0000000002"));
        Assert.True(exists);
    }

    [Fact]
    public async Task Any_is_false_when_no_match_exists()
    {
        var store = await SeededStoreAsync(Sample());
        var exists = await store.AnyAsync(Query<Doc>.Where(x => x.Category, QueryOp.Equal, "nope"));
        Assert.False(exists);
    }

    [Fact]
    public async Task Empty_store_returns_nothing()
    {
        var store = await SeededStoreAsync();
        var result = await store.QueryAsync(Query<Doc>.All());
        Assert.Empty(result);
    }

    [Fact]
    public async Task Collection_query_requires_deterministic_order_before_provider_io()
    {
        var documents = new InMemoryDocumentStore(BuildManifest());
        var store = new GroundworkReadStore<Doc>(
            documents,
            DocumentKind,
            ListAllQuery,
            CollectionField,
            CollectionValue,
            Json);

        var exception = await Assert.ThrowsAsync<GroundworkQueryReadinessException>(() =>
            store.QueryAsync(Query<Doc>.All()));

        Assert.Contains("deterministic", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, documents.DocumentQueryCount);
    }

    [Fact]
    public async Task Collection_query_exhausts_multiple_offset_pages_without_truncation()
    {
        var documents = Enumerable.Range(0, ElsaGroundworkQueryRoutes.MaximumResultCount + 1)
            .Select(number =>
            {
                var id = $"doc-{number:D4}";
                var content = JsonSerializer.Serialize(
                    new GroundworkDocument<Doc>(CollectionValue, new Doc { Id = id, Name = id }),
                    Json);
                return new DocumentEnvelope(
                    DocumentKind,
                    id,
                    SchemaVersion,
                    1,
                    content,
                    DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch);
            })
            .ToArray();
        var pages = new PagedBoundedDocumentStore(documents);

        var store = new GroundworkReadStore<Doc>(
            new InMemoryDocumentStore(BuildManifest()),
            DocumentKind,
            ListAllQuery,
            CollectionField,
            CollectionValue,
            Json,
            pages,
            collectionOrder: DocumentIdentityOrder);

        var result = await store.QueryAsync(Query<Doc>.All());

        Assert.Equal(ElsaGroundworkQueryRoutes.MaximumResultCount + 1, result.Count);
        Assert.Equal(2, pages.QueryCount);
    }

    [Fact]
    public async Task Continuation_pager_exhausts_multiple_pages()
    {
        var documents = new[] { Envelope("a"), Envelope("b") };
        var store = new ScriptedBoundedDocumentStore(
            new DocumentQueryResult([documents[0]], 2, "next"),
            new DocumentQueryResult([documents[1]], 2));

        var result = await BoundedDocumentQueryPager.QueryAllAsync(
            store,
            DocumentKind,
            ListAllQuery,
            [],
            CancellationToken.None);

        Assert.Equal(documents, result);
        Assert.Equal(2, store.QueryCount);
    }

    [Fact]
    public async Task Continuation_pager_rejects_a_repeated_continuation()
    {
        var store = new ScriptedBoundedDocumentStore(
            new DocumentQueryResult([Envelope("a")], 2, "same"),
            new DocumentQueryResult([Envelope("b")], 2, "same"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BoundedDocumentQueryPager.QueryAllAsync(
                store,
                DocumentKind,
                ListAllQuery,
                [],
                CancellationToken.None).AsTask());

        Assert.Contains("continuation", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, store.QueryCount);
    }

    [Fact]
    public async Task Continuation_pager_rejects_an_oversized_provider_page()
    {
        var oversized = Enumerable.Range(0, ElsaGroundworkQueryRoutes.MaximumResultCount + 1)
            .Select(number => Envelope($"doc-{number:D4}"))
            .ToArray();
        var store = new ScriptedBoundedDocumentStore(new DocumentQueryResult(oversized, oversized.Length));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BoundedDocumentQueryPager.QueryAllAsync(
                store,
                DocumentKind,
                ListAllQuery,
                [],
                CancellationToken.None).AsTask());

        Assert.Contains("exceeded", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Continuation_pager_honors_cancellation_before_provider_io()
    {
        var store = new ScriptedBoundedDocumentStore(new DocumentQueryResult([], 0));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            BoundedDocumentQueryPager.QueryAllAsync(
                store,
                DocumentKind,
                ListAllQuery,
                [],
                cancellation.Token).AsTask());

        Assert.Equal(0, store.QueryCount);
    }

    [Fact]
    public async Task Offset_pager_rejects_a_page_sequence_that_stops_before_the_reported_total()
    {
        var store = new StallingBoundedDocumentStore();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BoundedDocumentQueryPager.QueryAllOffsetAsync(
                store,
                DocumentKind,
                ListAllQuery,
                [],
                DocumentIdentityOrder,
                CancellationToken.None).AsTask());

        Assert.Contains("total count", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, store.QueryCount);
    }

    [Fact]
    public async Task Offset_pager_requires_a_declared_order_before_provider_io()
    {
        var store = new ScriptedBoundedDocumentStore(new DocumentQueryResult([], 0));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BoundedDocumentQueryPager.QueryAllOffsetAsync(
                store,
                DocumentKind,
                ListAllQuery,
                [],
                [],
                CancellationToken.None).AsTask());

        Assert.Contains("order", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, store.QueryCount);
    }

    [Fact]
    public async Task Offset_pager_rejects_an_oversized_provider_page()
    {
        var oversized = Enumerable.Range(0, ElsaGroundworkQueryRoutes.MaximumResultCount + 1)
            .Select(number => Envelope($"doc-{number:D4}"))
            .ToArray();
        var store = new ScriptedBoundedDocumentStore(new DocumentQueryResult(oversized, oversized.Length));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BoundedDocumentQueryPager.QueryAllOffsetAsync(
                store,
                DocumentKind,
                ListAllQuery,
                [],
                DocumentIdentityOrder,
                CancellationToken.None).AsTask());

        Assert.Contains("exceeded", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Offset_pager_rejects_a_negative_total_count()
    {
        var store = new ScriptedBoundedDocumentStore(new DocumentQueryResult([], -1));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BoundedDocumentQueryPager.QueryAllOffsetAsync(
                store,
                DocumentKind,
                ListAllQuery,
                [],
                DocumentIdentityOrder,
                CancellationToken.None).AsTask());

        Assert.Contains("negative", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Offset_pager_rejects_a_total_count_that_changes_between_pages()
    {
        var store = new ScriptedBoundedDocumentStore(
            new DocumentQueryResult([Envelope("a")], 2),
            new DocumentQueryResult([Envelope("b")], 3));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BoundedDocumentQueryPager.QueryAllOffsetAsync(
                store,
                DocumentKind,
                ListAllQuery,
                [],
                DocumentIdentityOrder,
                CancellationToken.None).AsTask());

        Assert.Contains("changed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, store.QueryCount);
    }

    [Fact]
    public async Task Offset_pager_rejects_pages_that_exceed_the_reported_total()
    {
        var store = new ScriptedBoundedDocumentStore(
            new DocumentQueryResult([Envelope("a"), Envelope("b")], 1));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BoundedDocumentQueryPager.QueryAllOffsetAsync(
                store,
                DocumentKind,
                ListAllQuery,
                [],
                DocumentIdentityOrder,
                CancellationToken.None).AsTask());

        Assert.Contains("total count", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Offset_pager_accepts_an_empty_exhausted_result()
    {
        var store = new ScriptedBoundedDocumentStore(new DocumentQueryResult([], 0));

        var result = await BoundedDocumentQueryPager.QueryAllOffsetAsync(
            store,
            DocumentKind,
            ListAllQuery,
            [],
            DocumentIdentityOrder,
            CancellationToken.None);

        Assert.Empty(result);
        Assert.Equal(1, store.QueryCount);
    }

    [Fact]
    public async Task Offset_pager_accepts_equal_document_ids_from_different_storage_scopes()
    {
        var documents = new[]
        {
            Envelope("shared", "tenant-a"),
            Envelope("shared", "tenant-b")
        };
        var store = new PagedBoundedDocumentStore(documents, pageSize: 1);

        var result = await BoundedDocumentQueryPager.QueryAllOffsetAsync(
            store,
            DocumentKind,
            ListAllQuery,
            [],
            DocumentIdentityOrder,
            CancellationToken.None);

        Assert.Equal(documents, result);
        Assert.Equal(2, store.QueryCount);
    }

    [Fact]
    public async Task Offset_pager_rejects_a_repeated_storage_identity()
    {
        var repeated = Envelope("shared", "tenant-a");
        var store = new RepeatingBoundedDocumentStore(repeated);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BoundedDocumentQueryPager.QueryAllOffsetAsync(
                store,
                DocumentKind,
                ListAllQuery,
                [],
                DocumentIdentityOrder,
                CancellationToken.None).AsTask());

        Assert.Contains("storage identity", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, store.QueryCount);
    }

    [Fact]
    public async Task Offset_pager_honors_cancellation_before_provider_io()
    {
        var store = new StallingBoundedDocumentStore();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            BoundedDocumentQueryPager.QueryAllOffsetAsync(
                store,
                DocumentKind,
                ListAllQuery,
                [],
                DocumentIdentityOrder,
                cancellation.Token).AsTask());

        Assert.Equal(0, store.QueryCount);
    }

    private static StorageManifest BuildManifest() => new(
        new StorageManifestIdentity("elsa-groundwork-querying-tests"),
        new StorageManifestOwner("elsa.persistence.groundwork.querying.tests"),
        new StorageManifestVersion(SchemaVersion),
        [
            new StorageUnit(
                new StorageUnitIdentity(DocumentKind),
                "Doc",
                StorageIntent.PortableDocument(),
                LifecyclePolicy.Mutable,
                IdentityPolicy.StringId(),
                TenancyPolicy.Global,
                ConcurrencyPolicy.Optimistic(),
                SerializationPolicy.Json(),
                [
                    new IndexDeclaration(
                        CollectionIndex,
                        [new IndexField(CollectionField)],
                        IndexValueKind.Keyword,
                        false,
                        true,
                        MissingValueBehavior.Excluded,
                        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })
                ],
                [
                    new PortableQueryDeclaration(
                        "list-all",
                        CollectionIndex,
                        new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
                        QuerySortSupport.Ascending,
                        QueryPagingSupport.Offset)
                ],
                PhysicalizationPolicy.Portable)
        ],
        new HashSet<string> { "optimistic-concurrency" },
        []);

    private sealed class DocumentStoreOnlyAdapter(IDocumentStore inner) : IDocumentStore
    {
        public TransactionBoundary TransactionBoundary => inner.TransactionBoundary;
        public DocumentStoreAccess Access => inner.Access;

        public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(request, cancellationToken);

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

#pragma warning disable GW0004 // The test double must implement the complete IDocumentStore bridge surface.
    private sealed class ThrowingDocumentStore(StorageManifest manifest, Exception exception) : IDocumentStore
    {
        private readonly InMemoryDocumentStore _inner = new(manifest);

        public TransactionBoundary TransactionBoundary => _inner.TransactionBoundary;
        public DocumentStoreAccess Access => _inner.Access;
        public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default) => _inner.SaveAsync(request, cancellationToken);
        public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default) => Task.FromException<DocumentEnvelope?>(exception);
        public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default) => _inner.DeleteAsync(request, cancellationToken);
        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default) => _inner.QueryAsync(query, cancellationToken);
        public Task<DocumentQueryResult> QueryAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) => _inner.QueryAsync(query, cancellationToken);
        public Task<DocumentEnvelope?> FirstOrDefaultAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) => _inner.FirstOrDefaultAsync(query, cancellationToken);
        public Task<bool> AnyAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) => _inner.AnyAsync(query, cancellationToken);
        public Task<IDocumentUnitOfWork> BeginAsync(DocumentCommitScope scope, CancellationToken cancellationToken = default) => _inner.BeginAsync(scope, cancellationToken);
    }
#pragma warning restore GW0004

    private sealed class ThrowingBoundedDocumentStore(Exception exception) : IBoundedDocumentStore
    {
        public Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default) => Task.FromException<DocumentQueryResult>(exception);
        public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) => Task.FromException<long>(exception);
        public Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) => Task.FromException<DocumentEnvelope?>(exception);
        public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) => Task.FromException<bool>(exception);
    }

    private sealed class StallingBoundedDocumentStore : IBoundedDocumentStore
    {
        private static readonly IReadOnlyList<DocumentEnvelope> Page = Enumerable.Range(0, ElsaGroundworkQueryRoutes.MaximumResultCount)
            .Select(number => new DocumentEnvelope(
                DocumentKind,
                $"duplicate-{number:D4}",
                SchemaVersion,
                1,
                "{}",
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch))
            .ToArray();

        public int QueryCount { get; private set; }

        public Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            QueryCount++;
            return Task.FromResult(QueryCount == 1
                ? new DocumentQueryResult(Page, Page.Count + 1L)
                : new DocumentQueryResult([], Page.Count + 1L));
        }

        public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult((long)Page.Count);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<DocumentEnvelope?>(Page[0]);

        public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class PagedBoundedDocumentStore(
        IReadOnlyList<DocumentEnvelope> documents,
        int? pageSize = null) : IBoundedDocumentStore
    {
        public int QueryCount { get; private set; }

        public Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            QueryCount++;
            var page = documents
                .Skip(query.Skip ?? 0)
                .Take(Math.Min(query.Take ?? documents.Count, pageSize ?? int.MaxValue))
                .ToArray();
            return Task.FromResult(new DocumentQueryResult(page, documents.Count));
        }

        public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult((long)documents.Count);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(documents.FirstOrDefault());

        public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(documents.Count != 0);
    }

    private sealed class RepeatingBoundedDocumentStore(DocumentEnvelope document) : IBoundedDocumentStore
    {
        public int QueryCount { get; private set; }

        public Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            QueryCount++;
            return Task.FromResult(new DocumentQueryResult([document], 2));
        }

        public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(2L);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<DocumentEnvelope?>(document);

        public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class ScriptedBoundedDocumentStore(params DocumentQueryResult[] results) : IBoundedDocumentStore
    {
        public int QueryCount { get; private set; }

        public Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (QueryCount >= results.Length)
                throw new InvalidOperationException("The pager requested an unexpected provider page.");
            return Task.FromResult(results[QueryCount++]);
        }

        public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(results.FirstOrDefault()?.TotalCount ?? 0);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(results.SelectMany(x => x.Documents).FirstOrDefault());

        public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(results.Any(x => x.Documents.Count != 0));
    }

    private static DocumentEnvelope Envelope(string id, string? scope = null) => new(
        DocumentKind,
        id,
        SchemaVersion,
        1,
        "{}",
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch)
    {
        Scope = scope is null ? null : new StorageScope(scope)
    };

    private sealed class UnsupportedDocConverter : JsonConverter<Doc>
    {
        public override Doc? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException("Doc materialization is unavailable.");

        public override void Write(Utf8JsonWriter writer, Doc value, JsonSerializerOptions options) =>
            throw new NotSupportedException("Doc serialization is unavailable.");
    }
}
