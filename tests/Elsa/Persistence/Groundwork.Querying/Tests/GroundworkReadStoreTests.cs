using System.Text.Json;
using Elsa.Persistence.Core.Queries;
using Elsa.Primitives.Entities;
using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;
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

        return new GroundworkReadStore<Doc>(store, DocumentKind, CollectionIndex, CollectionValue, Json);
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
                TenancyPolicy.None,
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
}
