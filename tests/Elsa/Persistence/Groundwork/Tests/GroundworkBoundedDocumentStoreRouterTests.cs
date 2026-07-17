using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkBoundedDocumentStoreRouterTests
{
    [Fact]
    public async Task Explain_routes_the_exact_query_to_its_document_kind_runtime()
    {
        var expected = new MarkerExplainer();
        var other = new MarkerExplainer();
        var router = new GroundworkBoundedDocumentStoreRouter(
        [
            KeyValuePair.Create<string, IBoundedDocumentStore>("expected", expected),
            KeyValuePair.Create<string, IBoundedDocumentStore>("other", other)
        ]);
        var query = Query("expected");

        var exception = await Assert.ThrowsAsync<MarkerException>(() => router.ExplainAsync(query));

        Assert.Same(query, expected.ExplainedQuery);
        Assert.Null(other.ExplainedQuery);
        Assert.Equal("explain", exception.Message);
    }

    [Fact]
    public void Resolve_plan_forwards_the_exact_query_and_requested_operation()
    {
        var expected = new MarkerExplainer();
        var router = new GroundworkBoundedDocumentStoreRouter(
            [KeyValuePair.Create<string, IBoundedDocumentStore>("expected", expected)]);
        var query = Query("expected");

        var exception = Assert.Throws<MarkerException>(() =>
            router.ResolvePlan(query, BoundedQueryResultOperation.Any));

        Assert.Same(query, expected.InspectedQuery);
        Assert.Equal(BoundedQueryResultOperation.Any, expected.InspectedOperation);
        Assert.Equal("resolve", exception.Message);
    }

    [Fact]
    public async Task Explain_fails_closed_for_unknown_kinds_and_non_explaining_runtimes()
    {
        var router = new GroundworkBoundedDocumentStoreRouter(
            [KeyValuePair.Create<string, IBoundedDocumentStore>("plain", new PlainBoundedStore())]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => router.ExplainAsync(Query("unknown")));
        await Assert.ThrowsAsync<NotSupportedException>(() => router.ExplainAsync(Query("plain")));
    }

    private static DocumentQuery Query(string documentKind) => new(
        documentKind,
        "by-value",
        [DocumentQueryClause.Of(DocumentQueryComparison.Equal("value", "target"))],
        take: 1);

    private sealed class MarkerExplainer : PlainBoundedStore, IPhysicalDocumentQueryExplainer
    {
        public DocumentQuery? ExplainedQuery { get; private set; }
        public DocumentQuery? InspectedQuery { get; private set; }
        public BoundedQueryResultOperation? InspectedOperation { get; private set; }

        public PhysicalQueryPlan ResolvePlan(
            DocumentQuery query,
            BoundedQueryResultOperation operation = BoundedQueryResultOperation.Documents)
        {
            InspectedQuery = query;
            InspectedOperation = operation;
            throw new MarkerException("resolve");
        }

        public Task<PhysicalDocumentQueryExplanation> ExplainAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default)
        {
            ExplainedQuery = query;
            throw new MarkerException("explain");
        }
    }

    private class PlainBoundedStore : IBoundedDocumentStore
    {
        public Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class MarkerException(string message) : Exception(message);
}
