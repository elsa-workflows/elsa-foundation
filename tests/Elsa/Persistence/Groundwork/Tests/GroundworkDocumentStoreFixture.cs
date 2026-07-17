using System.Globalization;
using System.Text.Json;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Groundwork.Sqlite.Documents;

namespace Elsa.Persistence.Groundwork.Tests;

internal sealed class GroundworkDocumentStoreFixture(
    IDocumentStore documentStore,
    IBoundedDocumentStore boundedDocumentStore,
    IAsyncDisposable? owner = null) : IAsyncDisposable
{
    private static readonly ProviderIdentity SqliteProvider = new("groundwork-sqlite", "1.0.0");

    public IDocumentStore DocumentStore { get; } = documentStore is IBoundedDocumentStore
        ? documentStore
        : new CombinedDocumentStore(documentStore, boundedDocumentStore);

    public IBoundedDocumentStore BoundedDocumentStore { get; } = boundedDocumentStore;

    public static GroundworkDocumentStoreFixture Create(
        string provider,
        StorageManifest? manifest = null,
        DocumentStoreAccess? access = null) => provider switch
        {
            "sqlite" => CreateSqlite("Data Source=:memory:", manifest, access),
            "memory" => CreateInMemory(manifest, access),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
        };

    public static GroundworkDocumentStoreFixture CreateSqlite(
        string connectionString,
        StorageManifest? manifest = null,
        DocumentStoreAccess? access = null)
    {
        TemporarySqliteDatabase? database = null;
        if (connectionString.Contains(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            database = new TemporarySqliteDatabase();
            connectionString = database.ConnectionString;
        }

        var selectedManifest = manifest ?? ElsaRuntimeStorageManifest.Create();
        var store = SqliteDocumentStoreFactory
            .CreateAsync(
                connectionString,
                selectedManifest,
                SqliteProvider,
                access ?? GroundworkTestAccess.ForManifest(selectedManifest))
            .GetAwaiter()
            .GetResult();

        return new GroundworkDocumentStoreFixture(
            store,
            new LegacyBoundedQueryAdapter(store, selectedManifest),
            database);
    }

    private static GroundworkDocumentStoreFixture CreateInMemory(
        StorageManifest? manifest,
        DocumentStoreAccess? access)
    {
        var store = new InMemoryDocumentStore(manifest ?? ElsaRuntimeStorageManifest.Create(), access);
        return new GroundworkDocumentStoreFixture(store, store);
    }

    public async ValueTask DisposeAsync()
    {
        if (owner is not null)
            await owner.DisposeAsync();
        else if (DocumentStore is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
    }

    private sealed class TemporarySqliteDatabase : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"elsa-groundwork-{Guid.NewGuid():N}.db");

        public string ConnectionString => $"Data Source={_path}";

        public ValueTask DisposeAsync()
        {
            File.Delete(_path);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Keeps the legacy factory-backed SQLite unit fixtures focused on adapter behavior while the
    /// production-host integration suites exercise Groundwork's physical bounded runtime directly.
    /// </summary>
    private sealed class LegacyBoundedQueryAdapter(IDocumentStore store, StorageManifest manifest)
        : IBoundedDocumentStore
    {
        public async Task<DocumentQueryResult> QueryAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default)
        {
            if (query.QueryIdentity == ElsaRuntimeStorageManifest.ListExpiredOpenWorkflowTestScopesQuery)
                return await QueryScopePageAsync(query, requireExpiry: true, cancellationToken);
            if (query.QueryIdentity == ElsaRuntimeStorageManifest.ListWorkflowTestScopesByStatePageQuery)
                return await QueryScopePageAsync(query, requireExpiry: false, cancellationToken);
            if (query.QueryIdentity == ElsaRuntimeStorageManifest.ListWorkflowDispatchesByTestScopeQuery)
                return await QuerySingleValuePageAsync(query, ElsaRuntimeStorageManifest.TestScopeIdField, cancellationToken);
            if (query.DocumentKind == ElsaRuntimeStorageManifest.BookmarkStateDocumentKind &&
                query.QueryIdentity == ElsaRuntimeStorageManifest.ListBookmarksByStimulusAndTypeQuery)
                return await QueryCompositeEqualityAsync(
                    query,
                    [
                        ElsaRuntimeStorageManifest.StimulusHashField,
                        ElsaRuntimeStorageManifest.StimulusTypeField
                    ],
                    cancellationToken);
            if (query.DocumentKind == ElsaRuntimeStorageManifest.DurableTimerDocumentKind &&
                query.QueryIdentity == ElsaRuntimeStorageManifest.ListDueDurableTimersQuery)
                return await QueryDateTimeLessThanOrEqualAsync(
                    query,
                    ElsaRuntimeStorageManifest.DurableTimerDueTimeField,
                    "Groundwork durable timer due cursor must be non-null.",
                    cancellationToken);
            if (query.DocumentKind == ElsaRuntimeStorageManifest.RecurringTriggerScheduleDocumentKind &&
                query.QueryIdentity == ElsaRuntimeStorageManifest.ListDueRecurringTriggerSchedulesQuery)
                return await QueryDateTimeLessThanOrEqualAsync(
                    query,
                    ElsaRuntimeStorageManifest.RecurringTriggerScheduleNextOccurrenceField,
                    "Groundwork recurring schedule due cursor must be non-null.",
                    cancellationToken);
            if (query.DocumentKind == ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind &&
                query.QueryIdentity == ElsaRuntimeStorageManifest.ListExpiredWorkflowExecutableSourceReferencesQuery)
                return await QueryNullableDateTimeLessThanOrEqualAsync(
                    query,
                    ElsaRuntimeStorageManifest.ExpiresAtField,
                    "Groundwork source-reference expiry cursor must be non-null.",
                    cancellationToken);
            if (TryGetBoundedQuery(query) is { PredicateFields.Count: > 1 } boundedQuery)
                return await QueryCompositeEqualityAsync(
                    query,
                    boundedQuery.PredicateFields.Select(field => field.Path).ToArray(),
                    cancellationToken);

            var (indexIdentity, value) = Resolve(query);
            var all = await store.QueryAsync(
                new DocumentStoreQuery(query.DocumentKind, indexIdentity, value),
                cancellationToken);
            IEnumerable<DocumentEnvelope> window = all;
            if (query.Skip is { } skip)
                window = window.Skip(skip);
            if (query.Take is { } take)
                window = window.Take(take);
            return new DocumentQueryResult(window.ToArray(), all.Count);
        }

        private async Task<DocumentQueryResult> QueryCompositeEqualityAsync(
            DocumentQuery query,
            IReadOnlyCollection<string> expectedPaths,
            CancellationToken cancellationToken)
        {
            var comparisons = query.Clauses.SelectMany(clause => clause.Comparisons).ToArray();
            if (comparisons.Length != expectedPaths.Count ||
                comparisons.Any(comparison =>
                    comparison.Operator != QueryComparisonOperator.Equal ||
                    comparison.Values.Count != 1 ||
                    !expectedPaths.Contains(comparison.Path, StringComparer.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Groundwork test query '{query.QueryIdentity}' has an unexpected shape.");
            }

#pragma warning disable GW0004
            var all = await store.QueryAsync(new PortableDocumentQuery(query.DocumentKind), cancellationToken);
#pragma warning restore GW0004
            var values = comparisons.ToDictionary(
                comparison => comparison.Path,
                comparison => comparison.Values[0] ??
                    throw new InvalidOperationException($"Groundwork test query '{query.QueryIdentity}' must have non-null comparison values."),
                StringComparer.Ordinal);
            var matches = all.Documents
                .Where(document => values.All(entry => StringComparer.Ordinal.Equals(ReadString(document, entry.Key), entry.Value)))
                .OrderBy(document => document.Id, StringComparer.Ordinal)
                .ToArray();
            IEnumerable<DocumentEnvelope> window = matches;
            if (query.Skip is { } skip)
                window = window.Skip(skip);
            if (query.Take is { } take)
                window = window.Take(take);
            return new DocumentQueryResult(window.ToArray(), matches.Length);
        }

        private async Task<DocumentQueryResult> QueryNullableDateTimeLessThanOrEqualAsync(
            DocumentQuery query,
            string expectedPath,
            string nullValueMessage,
            CancellationToken cancellationToken)
        {
            var comparison = query.Clauses
                .SelectMany(clause => clause.Comparisons)
                .Single();
            if (comparison.Operator != QueryComparisonOperator.LessThanOrEqual ||
                comparison.Path != expectedPath ||
                comparison.Values.Count != 1 ||
                query.Order.Count != 1 ||
                query.Order[0].Path != expectedPath)
            {
                throw new InvalidOperationException(
                    $"Groundwork test query '{query.QueryIdentity}' has an unexpected shape.");
            }

#pragma warning disable GW0004
            var all = await store.QueryAsync(new PortableDocumentQuery(query.DocumentKind), cancellationToken);
#pragma warning restore GW0004
            var asOf = DateTimeOffset.Parse(
                comparison.Values[0] ?? throw new InvalidOperationException(nullValueMessage),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);
            var matches = all.Documents
                .Select(document => (Document: document, ExpiresAt: ReadNullableDateTimeOffset(
                    document,
                    expectedPath)))
                .Where(entry => entry.ExpiresAt <= asOf)
                .OrderBy(entry => entry.ExpiresAt)
                .ThenBy(entry => entry.Document.Id, StringComparer.Ordinal)
                .ToArray();
            IEnumerable<DocumentEnvelope> window = matches.Select(entry => entry.Document);
            if (query.Skip is { } skip)
                window = window.Skip(skip);
            if (query.Take is { } take)
                window = window.Take(take);
            return new DocumentQueryResult(window.ToArray(), matches.Length);
        }

        private async Task<DocumentQueryResult> QuerySingleValuePageAsync(
            DocumentQuery query,
            string expectedPath,
            CancellationToken cancellationToken)
        {
            var comparison = query.Clauses
                .SelectMany(clause => clause.Comparisons)
                .Single();
            if (comparison.Operator != QueryComparisonOperator.Equal ||
                comparison.Path != expectedPath ||
                comparison.Values.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Groundwork test query '{query.QueryIdentity}' has an unexpected shape.");
            }

#pragma warning disable GW0004
            var all = await store.QueryAsync(new PortableDocumentQuery(query.DocumentKind), cancellationToken);
#pragma warning restore GW0004
            var expectedValue = comparison.Values[0];
            var matches = all.Documents
                .Where(document => StringComparer.Ordinal.Equals(ReadString(document, expectedPath), expectedValue))
                .OrderBy(document => document.Id, StringComparer.Ordinal)
                .ToArray();
            IEnumerable<DocumentEnvelope> window = matches;
            if (query.Skip is { } skip)
                window = window.Skip(skip);
            if (query.Take is { } take)
                window = window.Take(take);
            return new DocumentQueryResult(window.ToArray(), matches.Length);
        }

        private async Task<DocumentQueryResult> QueryScopePageAsync(
            DocumentQuery query,
            bool requireExpiry,
            CancellationToken cancellationToken)
        {
            var comparisons = query.Clauses.SelectMany(clause => clause.Comparisons).ToArray();
            var state = comparisons.Single(comparison => comparison.Path == ElsaRuntimeStorageManifest.StateField);
            var expiry = comparisons.SingleOrDefault(comparison => comparison.Path == ElsaRuntimeStorageManifest.ExpiresAtField);
            var afterScope = comparisons.SingleOrDefault(comparison => comparison.Path == ElsaRuntimeStorageManifest.ScopeIdField);
            if (state.Operator != QueryComparisonOperator.Equal || state.Values.Count != 1 ||
                requireExpiry && (expiry is null || expiry.Operator != QueryComparisonOperator.LessThanOrEqual || expiry.Values.Count != 1) ||
                afterScope is not null && (afterScope.Operator != QueryComparisonOperator.GreaterThan || afterScope.Values.Count != 1))
            {
                throw new InvalidOperationException("Groundwork test-scope page query has an unexpected shape.");
            }

            var stateValue = state.Values[0]
                ?? throw new InvalidOperationException("Groundwork test-scope state must be non-null.");
            var expiryValue = expiry is null
                ? (DateTimeOffset?)null
                : DateTimeOffset.Parse(
                    expiry.Values[0] ?? throw new InvalidOperationException("Groundwork test-scope expiry must be non-null."),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind);
            var afterScopeId = afterScope?.Values[0];
            var all = await store.QueryAsync(new PortableDocumentQuery(query.DocumentKind), cancellationToken);
            var candidates = all.Documents;
            var matches = candidates
                .Where(document => StringComparer.Ordinal.Equals(ReadState(document), stateValue))
                .Where(document => expiryValue is null || ReadExpiry(document) <= expiryValue)
                .Where(document => afterScopeId is null ||
                    StringComparer.Ordinal.Compare(ReadScopeId(document), afterScopeId) > 0)
                .OrderBy(ReadScopeId, StringComparer.Ordinal)
                .ToArray();
            IEnumerable<DocumentEnvelope> window = matches;
            if (query.Skip is { } skip)
                window = window.Skip(skip);
            if (query.Take is { } take)
                window = window.Take(take);
            return new DocumentQueryResult(window.ToArray(), matches.Length);
        }

        private async Task<DocumentQueryResult> QueryDateTimeLessThanOrEqualAsync(
            DocumentQuery query,
            string expectedPath,
            string nullValueMessage,
            CancellationToken cancellationToken)
        {
            var comparison = query.Clauses
                .SelectMany(clause => clause.Comparisons)
                .Single();
            if (comparison.Operator != QueryComparisonOperator.LessThanOrEqual ||
                comparison.Path != expectedPath ||
                comparison.Values.Count != 1 ||
                query.Order.Count != 1 ||
                query.Order[0].Path != expectedPath)
            {
                throw new InvalidOperationException(
                    $"Groundwork test query '{query.QueryIdentity}' has an unexpected shape.");
            }

#pragma warning disable GW0004
            var all = await store.QueryAsync(new PortableDocumentQuery(query.DocumentKind), cancellationToken);
#pragma warning restore GW0004
            var asOf = DateTimeOffset.Parse(
                comparison.Values[0] ?? throw new InvalidOperationException(nullValueMessage),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);
            var matches = all.Documents
                .Select(document => (Document: document, NextOccurrence: ReadDateTimeOffset(
                    document,
                    expectedPath)))
                .Where(entry => entry.NextOccurrence <= asOf)
                .OrderBy(entry => entry.NextOccurrence)
                .ThenBy(entry => entry.Document.Id, StringComparer.Ordinal)
                .ToArray();
            IEnumerable<DocumentEnvelope> window = matches.Select(entry => entry.Document);
            if (query.Skip is { } skip)
                window = window.Skip(skip);
            if (query.Take is { } take)
                window = window.Take(take);
            return new DocumentQueryResult(window.ToArray(), matches.Length);
        }

        private static DateTimeOffset ReadExpiry(DocumentEnvelope envelope)
        {
            using var document = JsonDocument.Parse(envelope.ContentJson);
            return document.RootElement.GetProperty(ElsaRuntimeStorageManifest.ExpiresAtField).GetDateTimeOffset();
        }

        private static string? ReadState(DocumentEnvelope envelope)
        {
            using var document = JsonDocument.Parse(envelope.ContentJson);
            return document.RootElement.GetProperty(ElsaRuntimeStorageManifest.StateField).GetString();
        }

        private static string ReadScopeId(DocumentEnvelope envelope)
        {
            using var document = JsonDocument.Parse(envelope.ContentJson);
            return document.RootElement.GetProperty(ElsaRuntimeStorageManifest.ScopeIdField).GetString()
                ?? throw new InvalidOperationException("Groundwork test-scope document requires scopeId.");
        }

        private static string? ReadString(DocumentEnvelope envelope, string path)
        {
            using var document = JsonDocument.Parse(envelope.ContentJson);
            var value = GetPropertyPath(document.RootElement, path);
            return value.ValueKind == JsonValueKind.Null ? null : value.GetString();
        }

        private static DateTimeOffset ReadDateTimeOffset(DocumentEnvelope envelope, string path)
        {
            using var document = JsonDocument.Parse(envelope.ContentJson);
            return GetPropertyPath(document.RootElement, path).GetDateTimeOffset();
        }

        private static DateTimeOffset? ReadNullableDateTimeOffset(DocumentEnvelope envelope, string path)
        {
            using var document = JsonDocument.Parse(envelope.ContentJson);
            var value = GetPropertyPath(document.RootElement, path);
            return value.ValueKind == JsonValueKind.Null ? null : value.GetDateTimeOffset();
        }

        private static JsonElement GetPropertyPath(JsonElement root, string path)
        {
            var current = root;
            foreach (var segment in path.Split('.'))
                current = current.GetProperty(segment);
            return current;
        }

        public async Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            (await QueryAsync(query, cancellationToken)).TotalCount;

        public async Task<DocumentEnvelope?> FirstOrDefaultAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            (await QueryAsync(query, cancellationToken)).Documents.FirstOrDefault();

        public async Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            await FirstOrDefaultAsync(query, cancellationToken) is not null;

        private (string IndexIdentity, string Value) Resolve(DocumentQuery query)
        {
            var unit = manifest.StorageUnits.Single(candidate => candidate.Identity.Value == query.DocumentKind);
            var indexIdentity = TryGetBoundedQuery(query)?.IndexIdentity
                                ?? unit.Queries.Single(candidate => candidate.Identity == query.QueryIdentity).IndexIdentity;
            var index = unit.Indexes.Single(candidate => candidate.Identity == indexIdentity);
            var clause = query.Clauses.Count == 1
                ? query.Clauses[0]
                : throw new InvalidOperationException($"Groundwork test query '{query.QueryIdentity}' must have one clause.");
            var comparison = clause.Comparisons.Count == 1
                ? clause.Comparisons[0]
                : throw new InvalidOperationException($"Groundwork test query '{query.QueryIdentity}' must have one comparison.");
            var indexField = index.Fields.Count == 1
                ? index.Fields[0]
                : throw new InvalidOperationException($"Groundwork test index '{indexIdentity}' must have one field.");
            if (comparison.Operator != QueryComparisonOperator.Equal || comparison.Path != indexField.Path || comparison.Values.Count != 1)
                throw new InvalidOperationException($"Groundwork test query '{query.QueryIdentity}' has an unexpected shape.");
            var value = comparison.Values[0]
                        ?? throw new InvalidOperationException($"Groundwork test query '{query.QueryIdentity}' must have a non-null comparison value.");
            return (indexIdentity, value);
        }

        private BoundedQueryDeclaration? TryGetBoundedQuery(DocumentQuery query)
        {
            var unit = manifest.StorageUnits.Single(candidate => candidate.Identity.Value == query.DocumentKind);
            return unit.PhysicalStorage?.BoundedQueries
                .SingleOrDefault(candidate => candidate.Identity == query.QueryIdentity);
        }
    }

    private sealed class CombinedDocumentStore(
        IDocumentStore documents,
        IBoundedDocumentStore queries) : IDocumentStore, IBoundedDocumentStore
    {
        public TransactionBoundary TransactionBoundary => documents.TransactionBoundary;
        public DocumentStoreAccess Access => documents.Access;

        public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default) =>
            documents.SaveAsync(request, cancellationToken);

        public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default) =>
            documents.LoadAsync(documentKind, id, cancellationToken);

        public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default) =>
            documents.DeleteAsync(request, cancellationToken);

        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default) =>
            documents.QueryAsync(query, cancellationToken);

        public Task<DocumentQueryResult> QueryAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            documents.QueryAsync(query, cancellationToken);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            documents.FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            documents.AnyAsync(query, cancellationToken);

        public Task<IDocumentUnitOfWork> BeginAsync(DocumentCommitScope scope, CancellationToken cancellationToken = default) =>
            documents.BeginAsync(scope, cancellationToken);

        Task<DocumentQueryResult> IBoundedDocumentStore.QueryAsync(DocumentQuery query, CancellationToken cancellationToken) =>
            queries.QueryAsync(query, cancellationToken);

        public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            queries.CountAsync(query, cancellationToken);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            queries.FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            queries.AnyAsync(query, cancellationToken);
    }
}
