using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
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
public sealed class InMemoryDocumentStore : IDocumentStore, IBoundedDocumentStore
{
    private readonly StorageManifest manifest;
    private readonly ConcurrentDictionary<(string Kind, string Id), DocumentEnvelope> _docs = new();
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _unitOfWorkGate = new(1, 1);
    private int _saveCount;
    private int _loadCount;
    private int _deleteCount;
    private int _beginCount;
    private int _documentQueryCount;

    public InMemoryDocumentStore(StorageManifest manifest, DocumentStoreAccess? access = null)
    {
        this.manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        Access = access ?? GroundworkTestAccess.ForManifest(manifest);
    }

    public DocumentStoreAccess Access { get; }
    public int SaveCount => Volatile.Read(ref _saveCount);
    public int LoadCount => Volatile.Read(ref _loadCount);
    public int DeleteCount => Volatile.Read(ref _deleteCount);
    public int BeginCount => Volatile.Read(ref _beginCount);
    public int DocumentQueryCount => Volatile.Read(ref _documentQueryCount);

    public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _saveCount);
        lock (_gate)
        {
            var key = (request.DocumentKind, request.Id);
            _docs.TryGetValue(key, out var existing);

            if (EvaluateSaveExpectedVersion(existing, request.ExpectedVersion) is { } failure)
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

            _docs[key] = envelope;
            return Task.FromResult(DocumentStoreWriteResult.Saved(envelope));
        }
    }

    // The published ExpectedVersion save matrix (Groundwork spec 014 amendment), matching the real providers
    // exactly: null = upsert; 0 = create-only (conflict when a document exists); any other value = CAS update
    // (conflict on version mismatch, NotFound against an absent document — a non-zero expectation can never match).
    private static DocumentStoreWriteResult? EvaluateSaveExpectedVersion(DocumentEnvelope? existing, long? expectedVersion)
    {
        if (expectedVersion is not { } expected)
            return null;
        if (existing is null)
            return expected == 0 ? null : DocumentStoreWriteResult.NotFound;
        return existing.Version == expected ? null : DocumentStoreWriteResult.ConcurrencyConflict;
    }

    public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _loadCount);
        return Task.FromResult(_docs.GetValueOrDefault((documentKind, id)));
    }

    // Test-only: enumerate the stored envelopes of a kind. The golden-fixture compatibility test uses
    // this to discover the composite document id the bridge assigned, so it can re-seed the same id under
    // the legacy schema stamp without re-implementing each store's id-composition scheme.
    public IReadOnlyCollection<DocumentEnvelope> Snapshot(string documentKind) =>
        _docs.Values.Where(d => d.DocumentKind == documentKind).ToArray();

    public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _deleteCount);
        lock (_gate)
        {
            var key = (request.DocumentKind, request.Id);
            if (!_docs.TryGetValue(key, out var existing))
                return Task.FromResult(DocumentStoreWriteResult.NotFound);
            if (request.ExpectedVersion is { } expected && existing.Version != expected)
                return Task.FromResult(DocumentStoreWriteResult.ConcurrencyConflict);
            _docs.TryRemove(key, out _);
            return Task.FromResult(DocumentStoreWriteResult.Deleted(existing.Id));
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

        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => element.GetString(),
            _ => element.ToString()
        };
    }

    // --- Closed-query (PortableDocumentQuery) surface. Only the clause-free "all documents of a kind" form
    // (with optional offset paging) is implemented — the shape the trigger-binding store's type-scoped scan
    // uses. A query carrying comparison clauses is still out of this double's remit. ---
    public Task<DocumentQueryResult> QueryAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default)
    {
        if (query.Clauses.Count > 0)
            throw new NotSupportedException("Clause-bearing PortableDocumentQuery is not exercised by this test double.");

        var all = _docs.Values
            .Where(d => d.DocumentKind == query.DocumentKind)
            .OrderBy(d => d.Id, StringComparer.Ordinal)
            .ToArray();

        var window = all.Skip(query.Skip ?? 0);
        if (query.Take is { } take)
            window = window.Take(take);

        return Task.FromResult(new DocumentQueryResult(window.ToArray(), all.Length));
    }

    public Task<DocumentEnvelope?> FirstOrDefaultAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("PortableDocumentQuery is not exercised by this test double.");

    public Task<bool> AnyAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("PortableDocumentQuery is not exercised by this test double.");

    public Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _documentQueryCount);
        var unit = manifest.StorageUnits.Single(candidate => candidate.Identity.Value == query.DocumentKind);
        var declaration = unit.PhysicalStorage?.BoundedQueries.SingleOrDefault(candidate => candidate.Identity == query.QueryIdentity);
        var legacyDeclaration = unit.Queries.SingleOrDefault(candidate => candidate.Identity == query.QueryIdentity);
        var indexIdentity = declaration?.IndexIdentity ?? legacyDeclaration?.IndexIdentity
            ?? throw new InvalidOperationException(
                $"Document kind '{query.DocumentKind}' does not declare bounded query '{query.QueryIdentity}'.");
        var logicalIndex = declaration is null
            ? null
            : unit.PhysicalStorage!.LogicalIndexes.Single(index => index.Identity == indexIdentity);
        var indexFields = logicalIndex?.Fields
            ?? unit.Indexes.Single(index => index.Identity == indexIdentity).Fields;
        var fieldKinds = indexFields.ToDictionary(
            field => field.Path,
            field => logicalIndex?.GetValueKind(field) ?? field.ValueKind,
            StringComparer.Ordinal);
        if (declaration is not null)
        {
            foreach (var residual in declaration.ResidualPredicateFields)
                fieldKinds[residual.Path] = residual.ValueKind;
        }
        var predicatePaths = declaration is null || declaration.PredicateFields.Count == 0
            ? indexFields.Select(field => field.Path)
            : declaration.PredicateFields.Select(field => field.Path);
        var paths = predicatePaths
            .Concat(declaration?.ResidualPredicateFields.Select(field => field.Path) ?? [])
            .Concat(declaration?.SortFields.Select(field => field.Path) ?? [])
            .ToHashSet(StringComparer.Ordinal);
        if (query.Clauses.SelectMany(clause => clause.Comparisons).Any(comparison => !paths.Contains(comparison.Path)))
            throw new InvalidOperationException("The bounded query contains an undeclared stable field path.");

        IEnumerable<DocumentEnvelope> matches = _docs.Values
            .Where(document => document.DocumentKind == query.DocumentKind);
        foreach (var clause in query.Clauses)
        {
            matches = matches.Where(document => clause.Comparisons.Any(comparison =>
                Matches(
                    ReadField(document.ContentJson, comparison.Path),
                    comparison,
                    fieldKinds[comparison.Path])));
        }

        IOrderedEnumerable<DocumentEnvelope> ordered = query.Order.Count > 0
            ? Order(
                matches,
                query.Order[0],
                fieldKinds)
            : matches.OrderBy(document => document.Id, StringComparer.Ordinal);
        foreach (var order in query.Order.Skip(1))
        {
            ordered = ThenOrder(ordered, order, fieldKinds);
        }
        IEnumerable<DocumentEnvelope> selected = ordered.ThenBy(document => document.Id, StringComparer.Ordinal);
        if (query.LatestPerKeyPath is { } latestPerKeyPath)
        {
            selected = selected
                .GroupBy(
                    document => Comparable(
                        ReadField(document.ContentJson, latestPerKeyPath),
                        fieldKinds[latestPerKeyPath]),
                    StringComparer.Ordinal)
                .Select(group => group.First());
        }

        var all = selected.ToArray();
        var window = all.Skip(query.Skip ?? 0);
        if (query.Take is { } take)
            window = window.Take(take);
        return Task.FromResult(new DocumentQueryResult(window.ToArray(), all.Length));
    }

    private static IOrderedEnumerable<DocumentEnvelope> Order(
        IEnumerable<DocumentEnvelope> documents,
        DocumentQueryOrder order,
        IReadOnlyDictionary<string, IndexValueKind?> fieldKinds) =>
        order.Direction == PhysicalSortDirection.Descending
            ? documents.OrderByDescending(
                document => Comparable(ReadField(document.ContentJson, order.Path), fieldKinds[order.Path]),
                StringComparer.Ordinal)
            : documents.OrderBy(
                document => Comparable(ReadField(document.ContentJson, order.Path), fieldKinds[order.Path]),
                StringComparer.Ordinal);

    private static IOrderedEnumerable<DocumentEnvelope> ThenOrder(
        IOrderedEnumerable<DocumentEnvelope> documents,
        DocumentQueryOrder order,
        IReadOnlyDictionary<string, IndexValueKind?> fieldKinds) =>
        order.Direction == PhysicalSortDirection.Descending
            ? documents.ThenByDescending(
                document => Comparable(ReadField(document.ContentJson, order.Path), fieldKinds[order.Path]),
                StringComparer.Ordinal)
            : documents.ThenBy(
                document => Comparable(ReadField(document.ContentJson, order.Path), fieldKinds[order.Path]),
                StringComparer.Ordinal);

    private static bool Matches(
        string? actual,
        DocumentQueryComparison comparison,
        IndexValueKind? valueKind)
    {
        if (comparison.Operator == QueryComparisonOperator.In)
        {
            return comparison.Values.Any(expected =>
                StringComparer.Ordinal.Equals(
                    Comparable(actual, valueKind),
                    Comparable(expected, valueKind)));
        }

        var expected = comparison.Values.SingleOrDefault();
        if (comparison.Operator == QueryComparisonOperator.NotEqual && expected is null)
            return actual is not null;
        if (comparison.Operator == QueryComparisonOperator.NotContains && (actual is null || expected is null))
            return actual is null && expected is not null;
        if (comparison.Operator != QueryComparisonOperator.Equal && (actual is null || expected is null))
            return false;
        var order = StringComparer.Ordinal.Compare(
            Comparable(actual, valueKind),
            Comparable(expected, valueKind));
        return comparison.Operator switch
        {
            QueryComparisonOperator.Equal => order == 0,
            QueryComparisonOperator.NotEqual => order != 0,
            QueryComparisonOperator.Contains => actual!.Contains(expected!, StringComparison.OrdinalIgnoreCase),
            QueryComparisonOperator.NotContains => !actual!.Contains(expected!, StringComparison.OrdinalIgnoreCase),
            QueryComparisonOperator.StartsWith => actual!.StartsWith(expected!, StringComparison.Ordinal),
            QueryComparisonOperator.GreaterThan => order > 0,
            QueryComparisonOperator.GreaterThanOrEqual => order >= 0,
            QueryComparisonOperator.LessThan => order < 0,
            QueryComparisonOperator.LessThanOrEqual => order <= 0,
            _ => throw new NotSupportedException($"The in-memory bounded-query test double does not support {comparison.Operator}.")
        };
    }

    private static string? Comparable(string? value, IndexValueKind? valueKind)
    {
        if (value is null)
            return value;

        return valueKind switch
        {
            IndexValueKind.DateTime => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                .UtcTicks.ToString("D19", CultureInfo.InvariantCulture),
            IndexValueKind.Boolean => bool.Parse(value).ToString().ToLowerInvariant(),
            _ => value
        };
    }

    public async Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        (await QueryAsync(query, cancellationToken)).TotalCount;

    public async Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
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

    // --- Document unit of work: an in-memory cross-document atomic batch mirroring the relational
    // provider's CrossUnitAtomic boundary (stage Save/Delete, read-your-writes, all-or-nothing commit). ---
    public TransactionBoundary TransactionBoundary => TransactionBoundary.CrossUnitAtomic;

    public async Task<IDocumentUnitOfWork> BeginAsync(DocumentCommitScope scope, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _beginCount);
        await _unitOfWorkGate.WaitAsync(cancellationToken);
        return new InMemoryDocumentUnitOfWork(this);
    }

    private sealed class InMemoryDocumentUnitOfWork(InMemoryDocumentStore store) : IDocumentUnitOfWork
    {
        private readonly List<Func<Task>> _pending = new();
        private readonly Dictionary<(string Kind, string Id), DocumentEnvelope?> _staged = new();
        private bool _completed;
        private int _disposed;

        public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default)
        {
            EnsureActive();
            var now = DateTimeOffset.UtcNow;
            var key = (request.DocumentKind, request.Id);
            var existing = ResolveCurrent(key);
            if (EvaluateSaveExpectedVersion(existing, request.ExpectedVersion) is { } failure)
                return Task.FromResult(failure);
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
            return Task.FromResult(DocumentStoreWriteResult.Deleted(existing.Id));
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
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return ValueTask.CompletedTask;
            // Dispose-without-commit rolls back: simply drop the staged operations.
            _completed = true;
            store._unitOfWorkGate.Release();
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
