using System.Text.Json;
using Elsa.Persistence.Core.Queries;
using Elsa.Primitives.Entities;
using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.Queries;
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

        return new GroundworkReadStore<Doc>(store, DocumentKind, ListAllQuery, CollectionField, CollectionValue, Json);
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

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => store.QueryAsync(Query<Doc>.All()));

        Assert.Contains(DocumentKind, exception.Message, StringComparison.Ordinal);
        Assert.Contains("bounded", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Malformed_document_reports_kind_and_id()
    {
        var documentStore = new InMemoryDocumentStore(BuildManifest());
        await documentStore.SaveAsync(new SaveDocumentRequest(DocumentKind, "bad", SchemaVersion, "null"));
        var store = new GroundworkReadStore<Doc>(documentStore, DocumentKind, ListAllQuery, CollectionField, CollectionValue, Json);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.FirstOrDefaultAsync(Query<Doc>.Where(x => x.Id, QueryOp.Equal, "bad")));

        Assert.Contains(DocumentKind, exception.Message);
        Assert.Contains("bad", exception.Message);
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
                        QuerySortSupport.None,
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
}
