using Elsa.Persistence.Groundwork.Stores;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Scoping;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Persistence.Groundwork.Querying.Tests;

/// <summary>
/// Direct guard coverage for <see cref="BoundedDocumentQueryPager"/> against misbehaving providers.
/// These objectives previously lived in the deleted transitional-reader suite; the pager remains
/// live production code behind every exhaustive design read, so a provider that returns oversized
/// pages, inconsistent totals, repeated continuations, or duplicate storage identities must keep
/// tripping a guarded failure instead of silently corrupting aggregate reads.
/// </summary>
public sealed class BoundedDocumentQueryPagerTests
{
    private const string DocumentKind = "doc";
    private const string ExhaustQuery = "exhaust-all";
    private const string SchemaVersion = "1.0.0";

    private static readonly IReadOnlyList<DocumentQueryOrder> DocumentIdentityOrder =
        [new(PhysicalDocumentFieldPaths.Id)];

    [Fact]
    public async Task Continuation_pager_exhausts_multiple_pages()
    {
        var documents = new[] { Envelope("a"), Envelope("b") };
        var store = new ScriptedBoundedDocumentStore(
            new DocumentQueryResult([documents[0]], 2, "next"),
            new DocumentQueryResult([documents[1]], 2));

        var result = await BoundedDocumentQueryPager.QueryAllAsync(
            store, DocumentKind, ExhaustQuery, [], CancellationToken.None);

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
                store, DocumentKind, ExhaustQuery, [], CancellationToken.None).AsTask());

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
                store, DocumentKind, ExhaustQuery, [], CancellationToken.None).AsTask());

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
                store, DocumentKind, ExhaustQuery, [], cancellation.Token).AsTask());

        Assert.Equal(0, store.QueryCount);
    }

    [Fact]
    public async Task Offset_pager_rejects_a_page_sequence_that_stops_before_the_reported_total()
    {
        var store = new StallingBoundedDocumentStore();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BoundedDocumentQueryPager.QueryAllOffsetAsync(
                store, DocumentKind, ExhaustQuery, [], DocumentIdentityOrder, CancellationToken.None).AsTask());

        Assert.Contains("total count", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, store.QueryCount);
    }

    [Fact]
    public async Task Offset_pager_requires_a_declared_order_before_provider_io()
    {
        var store = new ScriptedBoundedDocumentStore(new DocumentQueryResult([], 0));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BoundedDocumentQueryPager.QueryAllOffsetAsync(
                store, DocumentKind, ExhaustQuery, [], [], CancellationToken.None).AsTask());

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
                store, DocumentKind, ExhaustQuery, [], DocumentIdentityOrder, CancellationToken.None).AsTask());

        Assert.Contains("exceeded", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Offset_pager_rejects_a_negative_total_count()
    {
        var store = new ScriptedBoundedDocumentStore(new DocumentQueryResult([], -1));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BoundedDocumentQueryPager.QueryAllOffsetAsync(
                store, DocumentKind, ExhaustQuery, [], DocumentIdentityOrder, CancellationToken.None).AsTask());

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
                store, DocumentKind, ExhaustQuery, [], DocumentIdentityOrder, CancellationToken.None).AsTask());

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
                store, DocumentKind, ExhaustQuery, [], DocumentIdentityOrder, CancellationToken.None).AsTask());

        Assert.Contains("total count", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Offset_pager_accepts_an_empty_exhausted_result()
    {
        var store = new ScriptedBoundedDocumentStore(new DocumentQueryResult([], 0));

        var result = await BoundedDocumentQueryPager.QueryAllOffsetAsync(
            store, DocumentKind, ExhaustQuery, [], DocumentIdentityOrder, CancellationToken.None);

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
            store, DocumentKind, ExhaustQuery, [], DocumentIdentityOrder, CancellationToken.None);

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
                store, DocumentKind, ExhaustQuery, [], DocumentIdentityOrder, CancellationToken.None).AsTask());

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
                store, DocumentKind, ExhaustQuery, [], DocumentIdentityOrder, cancellation.Token).AsTask());

        Assert.Equal(0, store.QueryCount);
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
            cancellationToken.ThrowIfCancellationRequested();
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
}
