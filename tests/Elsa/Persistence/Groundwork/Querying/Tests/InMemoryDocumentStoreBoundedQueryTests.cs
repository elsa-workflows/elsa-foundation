using Groundwork.Core.Indexing;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;
using System.Text.Json;
using Xunit;

namespace Elsa.Persistence.Groundwork.Querying.Tests;

public sealed class InMemoryDocumentStoreBoundedQueryTests
{
    private const string DocumentKind = "bounded-query-document";
    private const string QueryIdentity = "search-bounded-query-documents";
    private const string NamePath = "entity.name";
    private const string ScorePath = "entity.score";
    private const string GroupPath = "entity.group";

    [Fact]
    public async Task Executes_every_declared_operator_and_terminal_operation_truthfully()
    {
        var store = new InMemoryDocumentStore(CreateManifest());
        await SaveAsync(store, "a", "alpha", 10, "selected");
        await SaveAsync(store, "b", "beta", 30, "selected");
        await SaveAsync(store, "c", "charlie", 20, "selected");
        await SaveAsync(store, "d", "delta", 40, "other");
        var query = ValidQuery(
            DocumentQueryComparison.NotContains(NamePath, "beta"),
            DocumentQueryComparison.Equal(GroupPath, "selected"));

        var page = await store.QueryAsync(query);
        var count = await store.CountAsync(query.Select(BoundedQueryResultOperation.Count));
        var first = await store.FirstOrDefaultAsync(query.Select(BoundedQueryResultOperation.First));
        var any = await store.AnyAsync(query.Select(BoundedQueryResultOperation.Any));

        Assert.Equal(["c", "a"], page.Documents.Select(document => document.Id));
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, count);
        Assert.Equal("c", first?.Id);
        Assert.True(any);
        Assert.Equal(4, store.DocumentQueryCount);
    }

    [Fact]
    public async Task StartsWith_matches_mixed_case_like_the_relational_dialects()
    {
        var store = new InMemoryDocumentStore(CreateManifest());
        await SaveAsync(store, "a", "AlphaRoute", 10, "selected");
        await SaveAsync(store, "b", "beta", 20, "selected");
        var query = ValidQuery(
            DocumentQueryComparison.StartsWith(NamePath, "aLPHa"),
            DocumentQueryComparison.Equal(GroupPath, "selected"));

        var page = await store.QueryAsync(query);

        Assert.Equal("a", Assert.Single(page.Documents).Id);
    }

    [Fact]
    public async Task Rejects_every_undeclared_runtime_shape_before_query_io()
    {
        var store = new InMemoryDocumentStore(CreateManifest());

        await RejectBeforeIoAsync(
            store,
            ValidQuery(
                new DocumentQueryComparison(
                    ScorePath,
                    QueryComparisonOperator.Contains,
                    ["2"]),
                DocumentQueryComparison.Equal(GroupPath, "selected")));
        await RejectBeforeIoAsync(
            store,
            new DocumentQuery(
                DocumentKind,
                QueryIdentity,
                [
                    DocumentQueryClause.AnyOf(
                        DocumentQueryComparison.Equal(NamePath, "alpha"),
                        DocumentQueryComparison.Equal(NamePath, "beta")),
                    DocumentQueryClause.Of(DocumentQueryComparison.Equal(GroupPath, "selected"))
                ],
                [new DocumentQueryOrder(ScorePath, PhysicalSortDirection.Descending)]));
        await RejectBeforeIoAsync(
            store,
            new DocumentQuery(
                DocumentKind,
                QueryIdentity,
                [DocumentQueryClause.Of(DocumentQueryComparison.Equal(GroupPath, "selected"))],
                [new DocumentQueryOrder(ScorePath, PhysicalSortDirection.Ascending)]));
        await RejectBeforeIoAsync(
            store,
            new DocumentQuery(
                DocumentKind,
                QueryIdentity,
                [DocumentQueryClause.Of(DocumentQueryComparison.Equal(GroupPath, "selected"))],
                continuation: "provider-token"));
        await RejectBeforeIoAsync(
            store,
            new DocumentQuery(
                DocumentKind,
                QueryIdentity,
                [DocumentQueryClause.Of(DocumentQueryComparison.Equal(GroupPath, "selected"))],
                latestPerKeyPath: NamePath));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.CountAsync(ValidQuery()));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.QueryAsync(new DocumentQuery(DocumentKind, "missing-query")));

        Assert.Equal(0, store.DocumentQueryCount);
    }

    [Fact]
    public async Task Rejects_document_results_without_a_page_limit_before_query_io()
    {
        var store = new InMemoryDocumentStore(CreateManifest());

        await RejectBeforeIoAsync(
            store,
            new DocumentQuery(
                DocumentKind,
                QueryIdentity,
                [DocumentQueryClause.Of(DocumentQueryComparison.Equal(GroupPath, "selected"))],
                [new DocumentQueryOrder(ScorePath, PhysicalSortDirection.Descending)]));
    }

    [Fact]
    public async Task Rejects_document_results_with_a_nonpositive_page_limit_before_query_io()
    {
        var store = new InMemoryDocumentStore(CreateManifest());

        await RejectBeforeIoAsync(
            store,
            new DocumentQuery(
                DocumentKind,
                QueryIdentity,
                [DocumentQueryClause.Of(DocumentQueryComparison.Equal(GroupPath, "selected"))],
                [new DocumentQueryOrder(ScorePath, PhysicalSortDirection.Descending)],
                take: 0));
    }

    [Fact]
    public async Task Physicalized_unit_rejects_a_missing_physical_route_instead_of_using_the_retained_legacy_route()
    {
        var store = new InMemoryDocumentStore(CreateManifest(includePhysicalRoute: false));

        await RejectBeforeIoAsync(
            store,
            new DocumentQuery(
                DocumentKind,
                QueryIdentity,
                [DocumentQueryClause.Of(DocumentQueryComparison.Equal(NamePath, "alpha"))],
                take: 1));
    }

    [Fact]
    public async Task Cancellation_is_observed_before_query_io()
    {
        var store = new InMemoryDocumentStore(CreateManifest());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.QueryAsync(ValidQuery(), cancellation.Token));

        Assert.Equal(0, store.DocumentQueryCount);
    }

    private static async Task RejectBeforeIoAsync(
        InMemoryDocumentStore store,
        DocumentQuery query)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.QueryAsync(query));
        Assert.Equal(0, store.DocumentQueryCount);
    }

    private static DocumentQuery ValidQuery(params DocumentQueryComparison[] comparisons) =>
        new(
            DocumentKind,
            QueryIdentity,
            comparisons.Select(DocumentQueryClause.Of).ToArray(),
            [new DocumentQueryOrder(ScorePath, PhysicalSortDirection.Descending)],
            take: 50);

    private static async Task SaveAsync(
        InMemoryDocumentStore store,
        string id,
        string name,
        int score,
        string group)
    {
        var content = JsonSerializer.Serialize(new { entity = new { name, score, group } });
        var result = await store.SaveAsync(new SaveDocumentRequest(DocumentKind, id, "1.0.0", content));
        Assert.Equal(DocumentStoreWriteStatus.Saved, result.Status);
    }

    private static StorageManifest CreateManifest(bool includePhysicalRoute = true)
    {
        var logicalIndex = new LogicalIndexDeclaration(
            "bounded-query-index",
            [
                new IndexField(ScorePath, IndexValueKind.Number),
                new IndexField(NamePath, IndexValueKind.String),
                new IndexField(GroupPath, IndexValueKind.Keyword)
            ],
            IndexValueKind.String,
            false,
            MissingValueBehavior.Excluded);
        var declaration = new BoundedQueryDeclaration(
            QueryIdentity,
            logicalIndex.Identity,
            new HashSet<PortableQueryOperation>
            {
                PortableQueryOperation.Equal,
                PortableQueryOperation.NotContains,
                PortableQueryOperation.StartsWith,
                PortableQueryOperation.GreaterThan
            },
            QuerySortSupport.Descending,
            QueryPagingSupport.Offset,
            supportsDisjunction: false,
            supportsTotalCount: true,
            sortFields:
            [
                new BoundedQuerySortField(ScorePath, PhysicalSortDirection.Descending),
                new BoundedQuerySortField(NamePath, PhysicalSortDirection.Ascending)
            ],
            predicateFields:
            [
                new BoundedQueryPredicateField(
                    NamePath,
                    new HashSet<PortableQueryOperation>
                    {
                        PortableQueryOperation.Equal,
                        PortableQueryOperation.NotContains,
                        PortableQueryOperation.StartsWith
                    }),
                new BoundedQueryPredicateField(
                    ScorePath,
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.GreaterThan })
            ],
            resultOperations: new HashSet<BoundedQueryResultOperation>
            {
                BoundedQueryResultOperation.Documents,
                BoundedQueryResultOperation.Count,
                BoundedQueryResultOperation.Any,
                BoundedQueryResultOperation.First
            },
            residualPredicateFields:
            [
                new BoundedQueryResidualPredicateField(
                    GroupPath,
                    IndexValueKind.Keyword,
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
                    isRequired: true)
            ]);

        var unit = StorageUnit.Create(
            new StorageUnitIdentity(DocumentKind),
            "Bounded query document",
            StorageIntent.PortableDocument(),
            LifecyclePolicy.Mutable,
            IdentityPolicy.StringId(),
            TenancyPolicy.Global,
            ConcurrencyPolicy.Optimistic(),
            SerializationPolicy.Json(),
            new StorageUnitPhysicalStorage(
                StorageUnitProvisioningMode.Declared,
                PhysicalStoragePolicy.Explicit(
                    PhysicalTableDefinition.PhysicalEntityTable(
                        DocumentKind,
                        [
                            new ProjectedColumnDefinition("name", NamePath, PortablePhysicalType.String, Length: 100),
                            new ProjectedColumnDefinition("score", ScorePath, PortablePhysicalType.Int32),
                            new ProjectedColumnDefinition("group", GroupPath, PortablePhysicalType.String, Length: 100)
                        ])),
                [logicalIndex],
                includePhysicalRoute ? [declaration] : []));

        return new StorageManifest(
            new StorageManifestIdentity("in-memory-bounded-query-tests"),
            new StorageManifestOwner("elsa.persistence.groundwork.querying.tests"),
            new StorageManifestVersion("1.0.0"),
            [unit],
            new HashSet<string> { "optimistic-concurrency" },
            []);
    }
}
