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

    [Theory]
    [InlineData("first")]
    [InlineData("any")]
    [InlineData("count")]
    [InlineData("query")]
    public async Task Terminal_methods_bind_the_result_operation_before_dispatch(string terminal)
    {
        var recording = new RecordingBoundedStore();
        var router = new GroundworkBoundedDocumentStoreRouter(
            [KeyValuePair.Create<string, IBoundedDocumentStore>("kind", recording)]);
        // A DocumentQuery declares BoundedQueryResultOperation.Documents by default; the caller does not
        // pre-select the terminal operation, exactly as the projection readers build their queries.
        var query = Query("kind");
        Assert.Equal(BoundedQueryResultOperation.Documents, query.ResultOperation);

        var expected = terminal switch
        {
            "first" => BoundedQueryResultOperation.First,
            "any" => BoundedQueryResultOperation.Any,
            "count" => BoundedQueryResultOperation.Count,
            _ => BoundedQueryResultOperation.Documents
        };
        _ = terminal switch
        {
            "first" => (object?)await router.FirstOrDefaultAsync(query),
            "any" => await router.AnyAsync(query),
            "count" => await router.CountAsync(query),
            _ => await router.QueryAsync(query)
        };

        Assert.Equal(expected, recording.ObservedOperation);
        // The rest of the query shape must survive the selection unchanged.
        Assert.Equal(query.QueryIdentity, recording.ObservedQuery!.QueryIdentity);
        Assert.Equal(query.Take, recording.ObservedQuery.Take);
        Assert.Equal(query.Clauses.Count, recording.ObservedQuery.Clauses.Count);
    }

    [Fact]
    public async Task First_or_default_satisfies_the_provider_native_result_operation_contract()
    {
        // Reproduces the container regression: the provider-native runtime rejects a query that does not
        // declare the terminal operation it is executed for. The router must select it so an unselected
        // query from a projection reader no longer faults startup.
        var strict = new ResultOperationEnforcingStore();
        var router = new GroundworkBoundedDocumentStoreRouter(
            [KeyValuePair.Create<string, IBoundedDocumentStore>("kind", strict)]);

        var envelope = await router.FirstOrDefaultAsync(Query("kind"));
        Assert.Null(envelope);
        Assert.True(await router.AnyAsync(Query("kind")) == false);
        Assert.Equal(0, await router.CountAsync(Query("kind")));
    }

    private static DocumentQuery Query(string documentKind) => new(
        documentKind,
        "by-value",
        [DocumentQueryClause.Of(DocumentQueryComparison.Equal("value", "target"))],
        take: 1);

    private sealed class RecordingBoundedStore : IBoundedDocumentStore
    {
        public DocumentQuery? ObservedQuery { get; private set; }
        public BoundedQueryResultOperation? ObservedOperation { get; private set; }

        public Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            Record(query);
            return Task.FromResult(new DocumentQueryResult([], 0));
        }

        public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            Record(query);
            return Task.FromResult(0L);
        }

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            Record(query);
            return Task.FromResult<DocumentEnvelope?>(null);
        }

        public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            Record(query);
            return Task.FromResult(false);
        }

        private void Record(DocumentQuery query)
        {
            ObservedQuery = query;
            ObservedOperation = query.ResultOperation;
        }
    }

    private sealed class ResultOperationEnforcingStore : IBoundedDocumentStore
    {
        public Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            Enforce(query, BoundedQueryResultOperation.Documents, new DocumentQueryResult([], 0));

        public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            Enforce(query, BoundedQueryResultOperation.Count, 0L);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            Enforce(query, BoundedQueryResultOperation.First, (DocumentEnvelope?)null);

        public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            Enforce(query, BoundedQueryResultOperation.Any, false);

        // Mirrors PhysicalQueryDocumentStore.Resolve: the query must declare the terminal operation.
        private static Task<T> Enforce<T>(DocumentQuery query, BoundedQueryResultOperation operation, T result) =>
            query.ResultOperation != operation
                ? throw new InvalidOperationException(
                    $"Document query '{query.QueryIdentity}' does not declare result operation '{operation}'.")
                : Task.FromResult(result);
    }

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
