using System.Collections.Concurrent;
using System.Text.Json;
using Groundwork.Core.Manifests;
using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Persistence.Groundwork.Testing;

/// <summary>
/// Shared in-memory <see cref="IDocumentStore"/> test double standing in for a Groundwork provider. It
/// implements the same provider-neutral contract the real relational/document providers implement and
/// resolves a query's index name to the declared field path from the manifest, exactly like the real
/// providers. This lets the Groundwork-backed store suites prove their bridges behave identically
/// regardless of which provider the host selects, rather than against a bespoke shim.
/// </summary>
/// <remarks>
/// This is the canonical superset of the previously duplicated per-suite copies: it honours optimistic
/// concurrency (<c>ExpectedVersion</c>), exposes <see cref="Snapshot"/> for the golden-fixture
/// compatibility tests, and provides a working cross-document unit of work mirroring the relational
/// provider's <see cref="TransactionBoundary.CrossUnitAtomic"/> boundary.
/// </remarks>
public sealed class InMemoryDocumentStore(StorageManifest manifest) : IDocumentStore
{
    private readonly ConcurrentDictionary<(string Kind, string Id), DocumentEnvelope> _docs = new();
    private readonly Lock _gate = new();

    public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var key = (request.DocumentKind, request.Id);
            _docs.TryGetValue(key, out var existing);

            if (request.ExpectedVersion is { } expected && (existing?.Version ?? 0) != expected)
                return Task.FromResult(DocumentStoreWriteResult.ConcurrencyConflict);

            var now = DateTimeOffset.UtcNow;
            var envelope = new DocumentEnvelope(
                request.DocumentKind,
                request.Id,
                request.SchemaVersion,
                (existing?.Version ?? 0) + 1,
                request.ContentJson,
                existing?.CreatedAt ?? now,
                now);

            _docs[key] = envelope;
            return Task.FromResult(DocumentStoreWriteResult.Saved(envelope));
        }
    }

    public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_docs.GetValueOrDefault((documentKind, id)));

    // Test-only: enumerate the stored envelopes of a kind. The golden-fixture compatibility test uses
    // this to discover the composite document id the bridge assigned, so it can re-seed the same id under
    // the legacy schema stamp without re-implementing each store's id-composition scheme.
    public IReadOnlyCollection<DocumentEnvelope> Snapshot(string documentKind) =>
        _docs.Values.Where(d => d.DocumentKind == documentKind).ToArray();

    public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var key = (request.DocumentKind, request.Id);
            if (!_docs.TryGetValue(key, out var existing))
                return Task.FromResult(DocumentStoreWriteResult.NotFound);
            if (request.ExpectedVersion is { } expected && existing.Version != expected)
                return Task.FromResult(DocumentStoreWriteResult.ConcurrencyConflict);
            _docs.TryRemove(key, out _);
            return Task.FromResult(DocumentStoreWriteResult.Deleted);
        }
    }

    public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default)
    {
        var fieldPath = ResolveIndexFieldPath(query.DocumentKind, query.IndexName);
        var matches = _docs.Values
            .Where(d => d.DocumentKind == query.DocumentKind)
            .Where(d => string.Equals(ReadField(d.ContentJson, fieldPath), query.Value, StringComparison.Ordinal))
            .OrderBy(d => d.Id, StringComparer.Ordinal)
            .Skip(query.Skip ?? 0);

        if (query.Take is { } take)
            matches = matches.Take(take);

        return Task.FromResult<IReadOnlyList<DocumentEnvelope>>(matches.ToArray());
    }

    private string ResolveIndexFieldPath(string documentKind, string indexName)
    {
        var unit = manifest.StorageUnits.Single(u => u.Identity.Value == documentKind);
        var index = unit.Indexes.SingleOrDefault(i => i.Identity == indexName)
            ?? throw new UndeclaredDocumentIndexException(documentKind, indexName);
        return index.Fields[0].Path;
    }

    private static string? ReadField(string contentJson, string path)
    {
        using var doc = JsonDocument.Parse(contentJson);
        var element = doc.RootElement;
        foreach (var segment in path.Split('.'))
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(segment, out element))
                return null;
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
    }

    // --- Closed-query (PortableDocumentQuery) surface: not exercised by these tests, which drive the
    // provider through the declared-index DocumentStoreQuery path. ---
    public Task<DocumentQueryResult> QueryAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("PortableDocumentQuery is not exercised by this test double.");

    public Task<DocumentEnvelope?> FirstOrDefaultAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("PortableDocumentQuery is not exercised by this test double.");

    public Task<bool> AnyAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("PortableDocumentQuery is not exercised by this test double.");

    // --- Document unit of work: an in-memory cross-document atomic batch mirroring the relational
    // provider's CrossUnitAtomic boundary (stage Save/Delete, read-your-writes, all-or-nothing commit). ---
    public TransactionBoundary TransactionBoundary => TransactionBoundary.CrossUnitAtomic;

    public Task<IDocumentUnitOfWork> BeginAsync(DocumentCommitScope scope, CancellationToken cancellationToken = default) =>
        Task.FromResult<IDocumentUnitOfWork>(new InMemoryDocumentUnitOfWork(this));

    private sealed class InMemoryDocumentUnitOfWork(InMemoryDocumentStore store) : IDocumentUnitOfWork
    {
        private readonly List<Func<Task>> _pending = new();
        private readonly Dictionary<(string Kind, string Id), DocumentEnvelope?> _staged = new();
        private bool _completed;

        public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default)
        {
            EnsureActive();
            var now = DateTimeOffset.UtcNow;
            var key = (request.DocumentKind, request.Id);
            var existing = ResolveCurrent(key);
            if (request.ExpectedVersion is { } expected && (existing?.Version ?? 0) != expected)
                return Task.FromResult(DocumentStoreWriteResult.ConcurrencyConflict);
            var envelope = new DocumentEnvelope(request.DocumentKind, request.Id, request.SchemaVersion,
                (existing?.Version ?? 0) + 1, request.ContentJson, existing?.CreatedAt ?? now, now);
            _staged[key] = envelope;
            _pending.Add(() => store.SaveAsync(request, cancellationToken));
            return Task.FromResult(DocumentStoreWriteResult.Saved(envelope));
        }

        public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default)
        {
            EnsureActive();
            var key = (request.DocumentKind, request.Id);
            var existing = ResolveCurrent(key);
            if (existing is null)
                return Task.FromResult(DocumentStoreWriteResult.NotFound);
            if (request.ExpectedVersion is { } expected && existing.Version != expected)
                return Task.FromResult(DocumentStoreWriteResult.ConcurrencyConflict);
            _staged[key] = null;
            _pending.Add(() => store.DeleteAsync(request, cancellationToken));
            return Task.FromResult(DocumentStoreWriteResult.Deleted);
        }

        public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default)
        {
            EnsureActive();
            return Task.FromResult(ResolveCurrent((documentKind, id)));
        }

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            EnsureActive();
            foreach (var op in _pending)
                await op();
            _completed = true;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            EnsureActive();
            _pending.Clear();
            _staged.Clear();
            _completed = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            // Dispose-without-commit rolls back: simply drop the staged operations.
            _completed = true;
            return ValueTask.CompletedTask;
        }

        private DocumentEnvelope? ResolveCurrent((string Kind, string Id) key) =>
            _staged.TryGetValue(key, out var staged) ? staged : store._docs.GetValueOrDefault(key);

        private void EnsureActive()
        {
            if (_completed)
                throw new InvalidOperationException("The unit of work has already completed.");
        }
    }
}
