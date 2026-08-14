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

        // Units that have migrated off the legacy declaration surface declare their indexes only on
        // their physical storage; the legacy list still answers for the ones that have not.
        var path = unit.Indexes.SingleOrDefault(i => i.Identity == indexName)?.Fields[0].Path
                   ?? unit.PhysicalStorage?.LogicalIndexes
                       .SingleOrDefault(i => i.Identity == indexName)?.Fields[0].Path;
        return path ?? throw new UndeclaredDocumentIndexException(documentKind, indexName);
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
        => ExecuteQueryAsync(query, BoundedQueryResultOperation.Documents, cancellationToken);

    private Task<DocumentQueryResult> ExecuteQueryAsync(
        DocumentQuery query,
        BoundedQueryResultOperation expectedOperation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var binding = ValidateQuery(query, expectedOperation);
        var fieldKinds = binding.FieldKinds;
        Interlocked.Increment(ref _documentQueryCount);

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
        return Task.FromResult(TestKeysetContinuations.Page(
            query,
            "in-memory",
            all,
            (document, path) => Comparable(ReadField(document.ContentJson, path), fieldKinds[path]),
            $"The continuation token is invalid for bounded query '{query.QueryIdentity}'."));
    }

    private QueryBinding ValidateQuery(
        DocumentQuery query,
        BoundedQueryResultOperation expectedOperation)
    {
        var unit = manifest.StorageUnits.SingleOrDefault(
            candidate => candidate.Identity.Value == query.DocumentKind)
            ?? throw new InvalidOperationException(
                $"Document kind '{query.DocumentKind}' is not declared by the in-memory manifest.");
        var declarations = unit.PhysicalStorage?.BoundedQueries
            .Where(candidate => candidate.Identity == query.QueryIdentity)
            .ToArray() ?? [];
        var legacyDeclarations = unit.Queries
            .Where(candidate => candidate.Identity == query.QueryIdentity)
            .ToArray();
        if ((unit.PhysicalStorage is not null && declarations.Length != 1) ||
            (unit.PhysicalStorage is null && legacyDeclarations.Length != 1))
        {
            throw new InvalidOperationException(
                $"Document kind '{query.DocumentKind}' must declare bounded query '{query.QueryIdentity}' exactly once.");
        }

        if (query.ResultOperation != expectedOperation)
        {
            throw new InvalidOperationException(
                $"Document query '{query.QueryIdentity}' requested result operation '{query.ResultOperation}' " +
                $"through the '{expectedOperation}' execution method.");
        }

        // Once a unit is physicalized, its bounded routes are authoritative. Retained legacy
        // declarations cannot hide a missing physical route.
        if (unit.PhysicalStorage is not null)
            return ValidatePhysicalQuery(unit, declarations[0], query, expectedOperation);

#pragma warning disable GW0003
        return ValidateLegacyQuery(unit, legacyDeclarations[0], query);
#pragma warning restore GW0003
    }

    private static QueryBinding ValidatePhysicalQuery(
        StorageUnit unit,
        BoundedQueryDeclaration declaration,
        DocumentQuery query,
        BoundedQueryResultOperation expectedOperation)
    {
        if (!declaration.ResultOperations.Contains(expectedOperation))
        {
            throw new InvalidOperationException(
                $"Document query '{query.QueryIdentity}' does not declare result operation '{expectedOperation}'.");
        }

        if (expectedOperation == BoundedQueryResultOperation.Documents && query.Take is not > 0)
        {
            throw new InvalidOperationException(
                $"Document query '{query.QueryIdentity}' must declare a positive page limit.");
        }

        var logicalIndex = unit.PhysicalStorage!.LogicalIndexes.Single(
            index => index.Identity == declaration.IndexIdentity);
        var predicates = declaration.PredicateFields.Count == 0
            ? new Dictionary<string, IReadOnlySet<PortableQueryOperation>>(StringComparer.Ordinal)
            {
                [logicalIndex.Fields[0].Path] = declaration.Operations
            }
            : declaration.PredicateFields.ToDictionary(
                field => field.Path,
                field => field.Operations,
                StringComparer.Ordinal);
        foreach (var residual in declaration.ResidualPredicateFields)
            predicates.Add(residual.Path, residual.Operations);

        ValidateClauses(query, declaration.SupportsDisjunction, predicates);
        ValidateRequiredResidualPredicates(query, declaration);
        ValidateOrder(query, declaration.SortFields);
        ValidatePaging(query, declaration.PagingSupport);
        ValidateLatestPerKey(query, declaration.LatestPerKeyPath);
        ValidateRequiredEqualityPrefixes(query, declaration, logicalIndex);

        var fieldKinds = logicalIndex.Fields.ToDictionary(
            field => field.Path,
            field => (IndexValueKind?)logicalIndex.GetValueKind(field),
            StringComparer.Ordinal);
        foreach (var residual in declaration.ResidualPredicateFields)
            fieldKinds[residual.Path] = residual.ValueKind;
        AddReferencedProjectedFieldKinds(unit, query, fieldKinds);
        return new QueryBinding(fieldKinds);
    }

#pragma warning disable GW0003
    private static QueryBinding ValidateLegacyQuery(
        StorageUnit unit,
        PortableQueryDeclaration declaration,
        DocumentQuery query)
#pragma warning restore GW0003
    {
#pragma warning disable GW0002
        var index = unit.Indexes.Single(candidate => candidate.Identity == declaration.IndexIdentity);
#pragma warning restore GW0002
        var predicates = index.Fields.ToDictionary(
            field => field.Path,
            _ => declaration.Operations,
            StringComparer.Ordinal);
        ValidateClauses(query, declaration.SupportsDisjunction, predicates);
        ValidateLegacyOrder(query, declaration, index.Fields);
        ValidatePaging(query, declaration.PagingSupport);
        if (query.LatestPerKeyPath is not null)
        {
            throw new InvalidOperationException(
                $"Latest-per-key selection is not bound to query '{query.QueryIdentity}'.");
        }

        var fieldKinds = index.Fields.ToDictionary(
            field => field.Path,
            field => field.ValueKind,
            StringComparer.Ordinal);
        AddReferencedProjectedFieldKinds(unit, query, fieldKinds);
        return new QueryBinding(fieldKinds);
    }

    private static void ValidateClauses(
        DocumentQuery query,
        bool supportsDisjunction,
        IReadOnlyDictionary<string, IReadOnlySet<PortableQueryOperation>> predicates)
    {
        foreach (var clause in query.Clauses)
        {
            if (clause.Comparisons.Count > 1 && !supportsDisjunction)
            {
                throw new InvalidOperationException(
                    $"Document query '{query.QueryIdentity}' does not declare disjunction.");
            }

            foreach (var comparison in clause.Comparisons)
            {
                var operation = ToPortableOperation(comparison.Operator);
                if (!predicates.TryGetValue(comparison.Path, out var operations) ||
                    !operations.Contains(operation))
                {
                    throw new InvalidOperationException(
                        $"Operation '{operation}' on path '{comparison.Path}' is not bound to query '{query.QueryIdentity}'.");
                }
            }
        }
    }

    private static void ValidateRequiredResidualPredicates(
        DocumentQuery query,
        BoundedQueryDeclaration declaration)
    {
        var supplied = query.Clauses
            .SelectMany(clause => clause.Comparisons)
            .Select(comparison => comparison.Path)
            .ToHashSet(StringComparer.Ordinal);
        var missing = declaration.ResidualPredicateFields
            .Where(field => field.IsRequired && !supplied.Contains(field.Path))
            .Select(field => field.Path)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (missing.Length != 0)
        {
            throw new InvalidOperationException(
                $"Document query '{query.QueryIdentity}' requires residual predicates: {string.Join(", ", missing)}.");
        }
    }

    private static void ValidateOrder(
        DocumentQuery query,
        IReadOnlyList<BoundedQuerySortField> declared)
    {
        if (query.Order.Count > declared.Count ||
            query.Order.Where((order, index) =>
                order.Path != declared[index].Path ||
                order.Direction != declared[index].Direction).Any())
        {
            throw new InvalidOperationException(
                $"Compound ordering exceeds query declaration '{query.QueryIdentity}'.");
        }
    }

#pragma warning disable GW0003, GW0002
    private static void ValidateLegacyOrder(
        DocumentQuery query,
        PortableQueryDeclaration declaration,
        IReadOnlyList<IndexField> indexFields)
#pragma warning restore GW0003, GW0002
    {
        if (query.Order.Count == 0)
            return;
        var direction = query.Order[0].Direction;
        var supported = query.Order.Count == 1 &&
                        query.Order[0].Path == indexFields[0].Path &&
                        declaration.SortSupport switch
                        {
                            QuerySortSupport.Ascending => direction == PhysicalSortDirection.Ascending,
                            QuerySortSupport.Descending => direction == PhysicalSortDirection.Descending,
                            QuerySortSupport.Both => true,
                            _ => false
                        };
        if (!supported)
        {
            throw new InvalidOperationException(
                $"Ordering is not bound to legacy query '{query.QueryIdentity}'.");
        }
    }

    private static void ValidatePaging(DocumentQuery query, QueryPagingSupport pagingSupport)
    {
        if (query.Skip is not null && pagingSupport != QueryPagingSupport.Offset)
        {
            throw new InvalidOperationException(
                $"Offset paging is not bound to query '{query.QueryIdentity}'.");
        }
        if (query.Continuation is not null && pagingSupport != QueryPagingSupport.Cursor)
        {
            throw new InvalidOperationException(
                $"Keyset paging is not bound to query '{query.QueryIdentity}'.");
        }
        if (query.Continuation is not null &&
            query.ResultOperation != BoundedQueryResultOperation.Documents)
        {
            throw new InvalidOperationException(
                "Keyset continuations apply only to document-page results.");
        }
    }

    private static void ValidateLatestPerKey(
        DocumentQuery query,
        string? declaredPath)
    {
        if (query.LatestPerKeyPath is not null &&
            query.LatestPerKeyPath != declaredPath)
        {
            throw new InvalidOperationException(
                $"Latest-per-key selection is not bound to query '{query.QueryIdentity}'.");
        }
    }

    private static void ValidateRequiredEqualityPrefixes(
        DocumentQuery query,
        BoundedQueryDeclaration declaration,
        LogicalIndexDeclaration logicalIndex)
    {
        if (declaration.SortFields.Count == 0)
            return;
        var firstSortIndex = logicalIndex.Fields
            .Select((field, index) => (field.Path, Index: index))
            .Single(item => item.Path == declaration.SortFields[0].Path)
            .Index;
        foreach (var path in logicalIndex.Fields.Take(firstSortIndex).Select(field => field.Path))
        {
            var occurrences = query.Clauses
                .SelectMany(clause => clause.Comparisons.Select(
                    comparison => (Clause: clause, Comparison: comparison)))
                .Where(item => item.Comparison.Path == path)
                .ToArray();
            if (occurrences.Length != 1 ||
                occurrences[0].Clause.Comparisons.Count != 1 ||
                occurrences[0].Comparison.Operator != QueryComparisonOperator.Equal)
            {
                throw new InvalidOperationException(
                    $"Ordered suffix query '{query.QueryIdentity}' requires exactly one standalone " +
                    $"equality comparison for prefix path '{path}'.");
            }
        }
    }

    private static void AddReferencedProjectedFieldKinds(
        StorageUnit unit,
        DocumentQuery query,
        IDictionary<string, IndexValueKind?> fieldKinds)
    {
        var projected = ProjectedFieldKinds(unit);
        foreach (var path in query.Clauses
                     .SelectMany(clause => clause.Comparisons)
                     .Select(comparison => comparison.Path)
                     .Concat(query.Order.Select(order => order.Path))
                     .Append(query.LatestPerKeyPath)
                     .Where(path => path is not null)
                     .Distinct(StringComparer.Ordinal))
        {
            if (fieldKinds.ContainsKey(path!))
                continue;
            if (!projected.TryGetValue(path!, out var kind))
            {
                throw new InvalidOperationException(
                    $"The bounded query field '{path}' has no declared manifest type.");
            }

            fieldKinds[path!] = kind;
        }
    }

    private static PortableQueryOperation ToPortableOperation(
        QueryComparisonOperator operation) => operation switch
    {
        QueryComparisonOperator.Equal => PortableQueryOperation.Equal,
        QueryComparisonOperator.In => PortableQueryOperation.In,
        QueryComparisonOperator.Contains => PortableQueryOperation.Contains,
        QueryComparisonOperator.NotContains => PortableQueryOperation.NotContains,
        QueryComparisonOperator.NotEqual => PortableQueryOperation.NotEqual,
        QueryComparisonOperator.StartsWith => PortableQueryOperation.StartsWith,
        QueryComparisonOperator.GreaterThan => PortableQueryOperation.GreaterThan,
        QueryComparisonOperator.GreaterThanOrEqual => PortableQueryOperation.GreaterThanOrEqual,
        QueryComparisonOperator.LessThan => PortableQueryOperation.LessThan,
        QueryComparisonOperator.LessThanOrEqual => PortableQueryOperation.LessThanOrEqual,
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
    };

    private sealed record QueryBinding(
        Dictionary<string, IndexValueKind?> FieldKinds);

    private static IReadOnlyDictionary<string, IndexValueKind> ProjectedFieldKinds(StorageUnit unit)
    {
        if (unit.PhysicalStorage?.Policy is not PhysicalStoragePolicy.ExplicitPolicy policy)
            return new Dictionary<string, IndexValueKind>(StringComparer.Ordinal);

        return policy.Definition.ProjectedColumns.ToDictionary(
            column => column.Path,
            column => column.Type switch
            {
                PortablePhysicalType.Boolean => IndexValueKind.Boolean,
                PortablePhysicalType.DateTime => IndexValueKind.DateTime,
                PortablePhysicalType.Decimal or PortablePhysicalType.Int32 or PortablePhysicalType.Int64 => IndexValueKind.Number,
                PortablePhysicalType.String => IndexValueKind.String,
                _ => throw new ArgumentOutOfRangeException(nameof(column.Type), column.Type, null)
            },
            StringComparer.Ordinal);
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
        if (comparison.Operator == QueryComparisonOperator.NotContains)
            return actual is null || !actual.Contains(expected!, StringComparison.OrdinalIgnoreCase);
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
            QueryComparisonOperator.StartsWith => actual!.StartsWith(expected!, StringComparison.OrdinalIgnoreCase),
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
            IndexValueKind.Number => NumberComparable(value),
            _ => value
        };
    }

    private static string NumberComparable(string value)
    {
        var number = decimal.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        var magnitude = decimal.Abs(number).ToString(
            "00000000000000000000000000000.0000000000000000000000000000",
            CultureInfo.InvariantCulture);
        if (number >= 0)
            return $"1{magnitude}";

        return string.Create(
            magnitude.Length + 1,
            magnitude,
            static (span, source) =>
            {
                span[0] = '0';
                for (var index = 0; index < source.Length; index++)
                {
                    var character = source[index];
                    span[index + 1] = character == '.'
                        ? character
                        : (char)('9' - (character - '0'));
                }
            });
    }

    public async Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        (await ExecuteQueryAsync(query, BoundedQueryResultOperation.Count, cancellationToken)).TotalCount;

    public async Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        (await ExecuteQueryAsync(
            new DocumentQuery(
                query.DocumentKind,
                query.QueryIdentity,
                query.Clauses,
                query.Order,
                query.Skip,
                1,
                query.Continuation,
                query.LatestPerKeyPath,
                query.ResultOperation),
            BoundedQueryResultOperation.First,
            cancellationToken)).Documents.FirstOrDefault();

    public async Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        (await ExecuteQueryAsync(
            new DocumentQuery(
                query.DocumentKind,
                query.QueryIdentity,
                query.Clauses,
                query.Order,
                query.Skip,
                1,
                query.Continuation,
                query.LatestPerKeyPath,
                query.ResultOperation),
            BoundedQueryResultOperation.Any,
            cancellationToken)).Documents.Count != 0;

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
