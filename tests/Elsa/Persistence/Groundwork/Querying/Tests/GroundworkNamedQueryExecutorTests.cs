using System.Text.Json;
using Elsa.Persistence.Core.Queries;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Primitives.Entities;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Core.Scoping;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Xunit;

namespace Elsa.Persistence.Groundwork.Querying.Tests;

public sealed class GroundworkNamedQueryExecutorTests
{
    private const string DocumentKind = "design";
    private const string QueryIdentity = "by-category";
    private const string SchemaVersion = "1.0.0";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyList<DocumentQueryOrder> ExactOrder =
    [
        new("entity.sortKey", PhysicalSortDirection.Descending),
        new(PhysicalDocumentFieldPaths.Id, PhysicalSortDirection.Descending)
    ];

    [Fact]
    public async Task Scoped_point_load_uses_the_session_document_store_and_materializes_the_canonical_envelope()
    {
        var provider = new ScriptedSessionStore { LoadResult = Envelope("one", "alpha", "0002") };
        await using var session = Session(provider);
        var executor = new GroundworkNamedQueryExecutor<DesignEntity>(session, DocumentKind, Json);

        var result = await executor.LoadAsync("one");

        Assert.Equal("alpha", result?.Category);
        Assert.Equal([(DocumentKind, "one")], provider.Loads);
        Assert.Empty(provider.Queries);
    }

    [Fact]
    public async Task Across_scope_point_load_is_rejected_before_provider_io()
    {
        var access = DocumentStoreAccess.PrivilegedAcrossScopes(
            new PrivilegedStorageAccess("catalog-maintenance"));
        var provider = new ScriptedSessionStore { Access = access };
        var recorder = new GroundworkPrivilegedAccessRecorder(new GroundworkPrivilegedAccessSink());
        await using var session = new GroundworkStoreSession(
            access,
            new GroundworkStoreSessionResources(provider, provider),
            recorder,
            recorder.RecordAcquisition(access));
        var executor = new GroundworkNamedQueryExecutor<DesignEntity>(session, DocumentKind, Json);

        var exception = await Assert.ThrowsAsync<GroundworkQueryReadinessException>(() =>
            executor.LoadAsync("one"));

        Assert.Equal(DocumentKind, exception.DocumentKind);
        Assert.Equal("one", exception.DocumentId);
        Assert.Empty(provider.Loads);
    }

    [Fact]
    public async Task First_translates_the_named_query_and_uses_the_full_declared_order()
    {
        var provider = new ScriptedSessionStore { FirstResult = Envelope("two", "alpha", "0002") };
        await using var session = Session(provider);
        var executor = new GroundworkNamedQueryExecutor<DesignEntity>(session, DocumentKind, Json);
        var query = Query<DesignEntity>
            .Where(x => x.Category, QueryOp.Equal, "alpha")
            .OrderByDescending(x => x.SortKey);

        var result = await executor.FirstOrDefaultAsync(QueryIdentity, query, ExactOrder);

        Assert.Equal("two", result?.Id);
        var sent = Assert.Single(provider.Queries);
        Assert.Equal(QueryIdentity, sent.QueryIdentity);
        Assert.Equal(BoundedQueryResultOperation.First, sent.ResultOperation);
        Assert.Equal(ExactOrder, sent.Order);
        var comparison = Assert.Single(Assert.Single(sent.Clauses).Comparisons);
        Assert.Equal("entity.category", comparison.Path);
        Assert.Equal(["alpha"], comparison.Values);
    }

    [Fact]
    public async Task Any_translates_the_named_query_and_uses_the_any_result_operation()
    {
        var provider = new ScriptedSessionStore { AnyResult = true };
        await using var session = Session(provider);
        var executor = new GroundworkNamedQueryExecutor<DesignEntity>(session, DocumentKind, Json);
        var query = Query<DesignEntity>.Where(x => x.Category, QueryOp.Equal, "alpha");

        var result = await executor.AnyAsync(QueryIdentity, query);

        Assert.True(result);
        var sent = Assert.Single(provider.Queries);
        Assert.Equal(BoundedQueryResultOperation.Any, sent.ResultOperation);
        Assert.Empty(sent.Order);
    }

    [Fact]
    public async Task Documents_exhaust_every_offset_page_with_the_full_declared_order()
    {
        var provider = new ScriptedSessionStore(
            new DocumentQueryResult([Envelope("two", "alpha", "0002")], 2),
            new DocumentQueryResult([Envelope("one", "alpha", "0001")], 2));
        await using var session = Session(provider);
        var executor = new GroundworkNamedQueryExecutor<DesignEntity>(session, DocumentKind, Json);
        var query = Query<DesignEntity>.Where(x => x.Category, QueryOp.Equal, "alpha");

        var result = await executor.QueryAsync(QueryIdentity, query, ExactOrder);

        Assert.Equal(["two", "one"], result.Select(x => x.Id));
        Assert.Equal(2, provider.Queries.Count);
        Assert.Equal([0, 1], provider.Queries.Select(x => x.Skip));
        Assert.All(provider.Queries, sent =>
        {
            Assert.Equal(BoundedQueryResultOperation.Documents, sent.ResultOperation);
            Assert.Equal(ExactOrder, sent.Order);
            Assert.Equal(ElsaGroundworkQueryRoutes.MaximumResultCount, sent.Take);
        });
    }

    [Fact]
    public async Task Incompatible_declared_order_fails_before_provider_io()
    {
        var provider = new ScriptedSessionStore();
        await using var session = Session(provider);
        var executor = new GroundworkNamedQueryExecutor<DesignEntity>(session, DocumentKind, Json);
        var query = Query<DesignEntity>
            .Where(x => x.Category, QueryOp.Equal, "alpha")
            .OrderByDescending(x => x.SortKey);
        IReadOnlyList<DocumentQueryOrder> incompatible =
            [new("entity.sortKey", PhysicalSortDirection.Ascending)];

        var exception = await Assert.ThrowsAsync<GroundworkQueryTranslationException>(() =>
            executor.FirstOrDefaultAsync(QueryIdentity, query, incompatible));

        Assert.Equal(DocumentKind, exception.DocumentKind);
        Assert.Equal(QueryIdentity, exception.QueryIdentity);
        Assert.Empty(provider.Queries);
    }

    [Fact]
    public async Task Corrupt_payload_reports_the_document_context()
    {
        var provider = new ScriptedSessionStore
        {
            FirstResult = new DocumentEnvelope(
                DocumentKind,
                "bad",
                SchemaVersion,
                1,
                "{",
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch)
        };
        await using var session = Session(provider);
        var executor = new GroundworkNamedQueryExecutor<DesignEntity>(session, DocumentKind, Json);

        var exception = await Assert.ThrowsAsync<GroundworkCorruptPayloadException>(() =>
            executor.FirstOrDefaultAsync(
                QueryIdentity,
                Query<DesignEntity>.Where(x => x.Category, QueryOp.Equal, "alpha")));

        Assert.Equal(DocumentKind, exception.DocumentKind);
        Assert.Equal("bad", exception.DocumentId);
        Assert.Equal(typeof(DesignEntity), exception.EntityType);
        Assert.IsType<JsonException>(exception.InnerException);
    }

    [Fact]
    public async Task Point_provider_failure_is_classified_without_losing_the_inner_exception()
    {
        var failure = new IOException("point failed");
        var provider = new ScriptedSessionStore { LoadFailure = failure };
        await using var session = Session(provider);
        var executor = new GroundworkNamedQueryExecutor<DesignEntity>(session, DocumentKind, Json);

        var exception = await Assert.ThrowsAsync<GroundworkProviderFailureException>(() =>
            executor.LoadAsync("one"));

        Assert.Equal(DocumentKind, exception.DocumentKind);
        Assert.Equal("one", exception.DocumentId);
        Assert.Null(exception.QueryIdentity);
        Assert.Same(failure, exception.InnerException);
    }

    [Fact]
    public async Task Bounded_provider_failure_is_classified_without_losing_the_inner_exception()
    {
        var failure = new IOException("query failed");
        var provider = new ScriptedSessionStore { BoundedFailure = failure };
        await using var session = Session(provider);
        var executor = new GroundworkNamedQueryExecutor<DesignEntity>(session, DocumentKind, Json);

        var exception = await Assert.ThrowsAsync<GroundworkProviderFailureException>(() =>
            executor.AnyAsync(
                QueryIdentity,
                Query<DesignEntity>.Where(x => x.Category, QueryOp.Equal, "alpha")));

        Assert.Equal(DocumentKind, exception.DocumentKind);
        Assert.Equal(QueryIdentity, exception.QueryIdentity);
        Assert.Same(failure, exception.InnerException);
    }

    [Fact]
    public async Task Cancellation_from_point_and_bounded_providers_is_not_wrapped()
    {
        var pointCancellation = new OperationCanceledException("point cancelled");
        var boundedCancellation = new OperationCanceledException("query cancelled");
        var pointProvider = new ScriptedSessionStore { LoadFailure = pointCancellation };
        var boundedProvider = new ScriptedSessionStore { BoundedFailure = boundedCancellation };
        await using var pointSession = Session(pointProvider);
        await using var boundedSession = Session(boundedProvider);
        var pointExecutor = new GroundworkNamedQueryExecutor<DesignEntity>(pointSession, DocumentKind, Json);
        var boundedExecutor = new GroundworkNamedQueryExecutor<DesignEntity>(boundedSession, DocumentKind, Json);

        var actualPoint = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            pointExecutor.LoadAsync("one"));
        var actualBounded = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            boundedExecutor.AnyAsync(
                QueryIdentity,
                Query<DesignEntity>.Where(x => x.Category, QueryOp.Equal, "alpha")));

        Assert.Same(pointCancellation, actualPoint);
        Assert.Same(boundedCancellation, actualBounded);
    }

    private static GroundworkStoreSession Session(ScriptedSessionStore provider) =>
        new(
            provider.Access,
            new GroundworkStoreSessionResources(provider, provider));

    private static DocumentEnvelope Envelope(string id, string category, string sortKey)
    {
        var entity = new DesignEntity { Id = id, Category = category, SortKey = sortKey };
        return new DocumentEnvelope(
            DocumentKind,
            id,
            SchemaVersion,
            1,
            JsonSerializer.Serialize(new GroundworkDocument<DesignEntity>(DocumentKind, entity), Json),
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);
    }

    private sealed class DesignEntity : Entity
    {
        public string Category { get; set; } = string.Empty;
        public string SortKey { get; set; } = string.Empty;
    }

#pragma warning disable GW0004 // The test double implements the complete compatibility document surface.
    private sealed class ScriptedSessionStore(params DocumentQueryResult[] pages) : IDocumentStore, IBoundedDocumentStore
    {
        private readonly Queue<DocumentQueryResult> _pages = new(pages);

        public DocumentStoreAccess Access { get; init; } =
            DocumentStoreAccess.Scoped(new StorageScope("tenant-a"));

        public TransactionBoundary TransactionBoundary => TransactionBoundary.PerOperation;

        public DocumentEnvelope? LoadResult { get; init; }

        public DocumentEnvelope? FirstResult { get; init; }

        public bool AnyResult { get; init; }

        public Exception? LoadFailure { get; init; }

        public Exception? BoundedFailure { get; init; }

        public List<(string Kind, string Id)> Loads { get; } = [];

        public List<DocumentQuery> Queries { get; } = [];

        public Task<DocumentEnvelope?> LoadAsync(
            string documentKind,
            string id,
            CancellationToken cancellationToken = default)
        {
            Loads.Add((documentKind, id));
            return LoadFailure is null
                ? Task.FromResult(LoadResult)
                : Task.FromException<DocumentEnvelope?>(LoadFailure);
        }

        public Task<DocumentQueryResult> QueryAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            if (BoundedFailure is not null)
                return Task.FromException<DocumentQueryResult>(BoundedFailure);
            return Task.FromResult(_pages.Count == 0
                ? new DocumentQueryResult([], 0)
                : _pages.Dequeue());
        }

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return BoundedFailure is null
                ? Task.FromResult(FirstResult)
                : Task.FromException<DocumentEnvelope?>(BoundedFailure);
        }

        public Task<bool> AnyAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return BoundedFailure is null
                ? Task.FromResult(AnyResult)
                : Task.FromException<bool>(BoundedFailure);
        }

        public Task<long> CountAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentStoreWriteResult> SaveAsync(
            SaveDocumentRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentStoreWriteResult> DeleteAsync(
            DeleteDocumentRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(
            DocumentStoreQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentQueryResult> QueryAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> AnyAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IDocumentUnitOfWork> BeginAsync(
            DocumentCommitScope scope,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
#pragma warning restore GW0004
}
