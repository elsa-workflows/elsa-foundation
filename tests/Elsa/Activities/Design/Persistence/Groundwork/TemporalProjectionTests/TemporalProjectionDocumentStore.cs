using System.Collections.Concurrent;
using System.Text.Json;
using Elsa.Locking.Core;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Activities.Design.Persistence.Groundwork.TemporalProjectionTests;

internal sealed class TemporalProjectionDocumentStore : IDocumentStore, IBoundedDocumentStore
{
    private readonly ConcurrentDictionary<(string Kind, string Id), DocumentEnvelope> _documents = new();
    private readonly Lock _gate = new();
    private readonly List<DocumentQuery> _queries = [];

    public int QueryCount { get; private set; }

    public IReadOnlyList<DocumentQuery> Queries => _queries;

    public string? FailOnDocumentKind { get; set; }

    public IReadOnlyList<DocumentEnvelope> Documents(string documentKind) =>
        _documents.Values
            .Where(x => StringComparer.Ordinal.Equals(x.DocumentKind, documentKind))
            .OrderBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();

    public void ResetQueries()
    {
        QueryCount = 0;
        _queries.Clear();
    }

    public DocumentStoreAccess Access => DocumentStoreAccess.Global;

    public TransactionBoundary TransactionBoundary => TransactionBoundary.CrossUnitAtomic;

    public Task<DocumentStoreWriteResult> SaveAsync(
        SaveDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var key = (request.DocumentKind, request.Id);
            _documents.TryGetValue(key, out var existing);
            if (ExpectedVersionFailure(existing, request.ExpectedVersion) is { } failure)
                return Task.FromResult(failure);
            var now = DateTimeOffset.UtcNow;
            var envelope = new DocumentEnvelope(
                request.DocumentKind,
                request.Id,
                request.SchemaVersion,
                (existing?.Version ?? 0) + 1,
                request.ContentJson,
                existing?.CreatedAt ?? now,
                now);
            _documents[key] = envelope;
            return Task.FromResult(DocumentStoreWriteResult.Saved(envelope));
        }
    }

    public Task<DocumentEnvelope?> LoadAsync(
        string documentKind,
        string id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_documents.GetValueOrDefault((documentKind, id)));

    public Task<DocumentStoreWriteResult> DeleteAsync(
        DeleteDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_documents.TryGetValue((request.DocumentKind, request.Id), out var existing))
                return Task.FromResult(DocumentStoreWriteResult.NotFound);
            if (request.ExpectedVersion is { } expected && existing.Version != expected)
                return Task.FromResult(DocumentStoreWriteResult.ConcurrencyConflict);
            _documents.TryRemove((request.DocumentKind, request.Id), out _);
            return Task.FromResult(DocumentStoreWriteResult.Deleted(request.Id));
        }
    }

    #pragma warning disable GW0004 // Required IDocumentStore compatibility members; the portable query surface is retired but still abstract.
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
    #pragma warning restore GW0004

    public Task<DocumentQueryResult> QueryAsync(
        DocumentQuery query,
        CancellationToken cancellationToken = default)
    {
        QueryCount++;
        _queries.Add(query);
        IEnumerable<DocumentEnvelope> matches = _documents.Values
            .Where(x => x.DocumentKind == query.DocumentKind);
        foreach (var clause in query.Clauses)
            matches = matches.Where(document => clause.Comparisons.Any(comparison => Matches(document, comparison)));

        IOrderedEnumerable<DocumentEnvelope>? ordered = null;
        foreach (var order in query.Order)
        {
            Func<DocumentEnvelope, string?> key = document => ReadField(document.ContentJson, order.Path);
            ordered = ordered is null
                ? order.Direction == global::Groundwork.Core.PhysicalStorage.PhysicalSortDirection.Descending
                    ? matches.OrderByDescending(key, StringComparer.Ordinal)
                    : matches.OrderBy(key, StringComparer.Ordinal)
                : order.Direction == global::Groundwork.Core.PhysicalStorage.PhysicalSortDirection.Descending
                    ? ordered.ThenByDescending(key, StringComparer.Ordinal)
                    : ordered.ThenBy(key, StringComparer.Ordinal);
        }
        var all = (ordered ?? matches.OrderBy(x => x.Id, StringComparer.Ordinal)).ToArray();
        var page = all.Skip(query.Skip ?? 0);
        if (query.Take is { } take)
            page = page.Take(take);
        return Task.FromResult(new DocumentQueryResult(page.ToArray(), all.LongLength));
    }

    public async Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        (await QueryAsync(query, cancellationToken)).TotalCount;

    public async Task<DocumentEnvelope?> FirstOrDefaultAsync(
        DocumentQuery query,
        CancellationToken cancellationToken = default) =>
        (await QueryAsync(new DocumentQuery(
            query.DocumentKind,
            query.QueryIdentity,
            query.Clauses,
            query.Order,
            query.Skip,
            1,
            query.Continuation,
            query.LatestPerKeyPath,
            query.ResultOperation), cancellationToken)).Documents.FirstOrDefault();

    public async Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        await FirstOrDefaultAsync(query, cancellationToken) is not null;

    public Task<IDocumentUnitOfWork> BeginAsync(
        DocumentCommitScope scope,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IDocumentUnitOfWork>(new UnitOfWork(this));

    private static bool Matches(DocumentEnvelope document, DocumentQueryComparison comparison)
    {
        var field = ReadField(document.ContentJson, comparison.Path);
        return comparison.Operator switch
        {
            QueryComparisonOperator.Equal => comparison.Values.Any(value => StringComparer.Ordinal.Equals(field, value)),
            QueryComparisonOperator.Contains => field?.Contains(comparison.Values[0]!, StringComparison.Ordinal) == true,
            QueryComparisonOperator.LessThanOrEqual => StringComparer.Ordinal.Compare(field, comparison.Values[0]) <= 0,
            QueryComparisonOperator.GreaterThan => StringComparer.Ordinal.Compare(field, comparison.Values[0]) > 0,
            _ => throw new NotSupportedException($"Comparison '{comparison.Operator}' is not implemented by the temporal test provider.")
        };
    }

    private static string? ReadField(string json, string path)
    {
        using var document = JsonDocument.Parse(json);
        var element = document.RootElement;
        foreach (var segment in path.Split('.'))
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(segment, out element))
                return null;
        }
        return element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
    }

    private static DocumentStoreWriteResult? ExpectedVersionFailure(
        DocumentEnvelope? existing,
        long? expectedVersion)
    {
        if (expectedVersion is not { } expected)
            return null;
        if (existing is null)
            return expected == 0 ? null : DocumentStoreWriteResult.NotFound;
        return existing.Version == expected ? null : DocumentStoreWriteResult.ConcurrencyConflict;
    }

    private sealed class UnitOfWork(TemporalProjectionDocumentStore store) : IDocumentUnitOfWork
    {
        private readonly Dictionary<(string Kind, string Id), DocumentEnvelope?> _staged = new();
        private bool _completed;

        public Task<DocumentStoreWriteResult> SaveAsync(
            SaveDocumentRequest request,
            CancellationToken cancellationToken = default)
        {
            EnsureActive();
            if (StringComparer.Ordinal.Equals(request.DocumentKind, store.FailOnDocumentKind))
                return Task.FromResult(DocumentStoreWriteResult.ConcurrencyConflict);
            var key = (request.DocumentKind, request.Id);
            var existing = Current(key);
            if (ExpectedVersionFailure(existing, request.ExpectedVersion) is { } failure)
                return Task.FromResult(failure);
            var now = DateTimeOffset.UtcNow;
            var envelope = new DocumentEnvelope(
                request.DocumentKind,
                request.Id,
                request.SchemaVersion,
                (existing?.Version ?? 0) + 1,
                request.ContentJson,
                existing?.CreatedAt ?? now,
                now);
            _staged[key] = envelope;
            return Task.FromResult(DocumentStoreWriteResult.Saved(envelope));
        }

        public Task<DocumentStoreWriteResult> DeleteAsync(
            DeleteDocumentRequest request,
            CancellationToken cancellationToken = default)
        {
            EnsureActive();
            var key = (request.DocumentKind, request.Id);
            var existing = Current(key);
            if (existing is null)
                return Task.FromResult(DocumentStoreWriteResult.NotFound);
            if (request.ExpectedVersion is { } expected && existing.Version != expected)
                return Task.FromResult(DocumentStoreWriteResult.ConcurrencyConflict);
            _staged[key] = null;
            return Task.FromResult(DocumentStoreWriteResult.Deleted(request.Id));
        }

        public Task<DocumentEnvelope?> LoadAsync(
            string documentKind,
            string id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Current((documentKind, id)));

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            EnsureActive();
            lock (store._gate)
            {
                foreach (var (key, envelope) in _staged)
                {
                    if (envelope is null)
                        store._documents.TryRemove(key, out _);
                    else
                        store._documents[key] = envelope;
                }
            }
            _completed = true;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            EnsureActive();
            _staged.Clear();
            _completed = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _completed = true;
            return ValueTask.CompletedTask;
        }

        private DocumentEnvelope? Current((string Kind, string Id) key) =>
            _staged.TryGetValue(key, out var staged) ? staged : store._documents.GetValueOrDefault(key);

        private void EnsureActive()
        {
            if (_completed)
                throw new InvalidOperationException("The unit of work has already completed.");
        }
    }
}

internal sealed class ImmediateDistributedLockProvider : IDistributedLockProvider
{
    public IDistributedSynchronizationHandle? TryAcquireLock(
        string name,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) => new Handle();

    public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(
        string name,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IDistributedSynchronizationHandle?>(new Handle());

    public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(
        string name,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IDistributedSynchronizationHandle>(new Handle());

    private sealed class Handle : IDistributedSynchronizationHandle
    {
        public CancellationToken HandleLostToken => CancellationToken.None;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
