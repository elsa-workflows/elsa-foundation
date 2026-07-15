using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
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

    public static GroundworkDocumentStoreFixture Create(string provider, StorageManifest? manifest = null) => provider switch
    {
        "sqlite" => CreateSqlite("Data Source=:memory:", manifest),
        "memory" => CreateInMemory(manifest),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
    };

    public static GroundworkDocumentStoreFixture CreateSqlite(string connectionString, StorageManifest? manifest = null)
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
                DocumentStoreAccess.Global)
            .GetAwaiter()
            .GetResult();

        return new GroundworkDocumentStoreFixture(
            store,
            new LegacyBoundedQueryAdapter(store, selectedManifest),
            database);
    }

    private static GroundworkDocumentStoreFixture CreateInMemory(StorageManifest? manifest)
    {
        var store = new InMemoryDocumentStore(manifest ?? ElsaRuntimeStorageManifest.Create());
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
            var indexIdentity = unit.PhysicalStorage?.BoundedQueries
                                    .SingleOrDefault(candidate => candidate.Identity == query.QueryIdentity)
                                    ?.IndexIdentity
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
