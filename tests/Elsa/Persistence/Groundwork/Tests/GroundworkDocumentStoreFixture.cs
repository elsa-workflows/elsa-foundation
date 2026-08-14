using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Persistence.Groundwork.Tests;

internal sealed class GroundworkDocumentStoreFixture(
    IDocumentStore documentStore,
    IBoundedDocumentStore boundedDocumentStore,
    IAsyncDisposable? owner = null) : IAsyncDisposable
{
    private static readonly ProviderIdentity SqliteProvider = new("groundwork-sqlite", "1.0.0");

    public IDocumentStore DocumentStore { get; } =
        documentStore is IBoundedDocumentStore && ReferenceEquals(documentStore, boundedDocumentStore)
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

        var selectedManifest = manifest ?? RuntimeManifest();
        // The provider itself serves the bounded routes: the store is opened on the physical surface and
        // every declared storage route is bound to the same compiled query runtime a production host uses.
        var (store, queries) = GroundworkPhysicalTestStores
            .OpenSqliteAsync(
                connectionString,
                selectedManifest,
                SqliteProvider,
                access ?? GroundworkTestAccess.ForManifest(selectedManifest))
            .GetAwaiter()
            .GetResult();

        return new GroundworkDocumentStoreFixture(store, queries, database);
    }

    private static GroundworkDocumentStoreFixture CreateInMemory(
        StorageManifest? manifest,
        DocumentStoreAccess? access)
    {
        // The in-memory double evaluates bounded queries natively against the same declared routes. It is
        // reached through the same router a provider host uses, so terminal result operations are selected
        // exactly as they are in production rather than by the caller.
        var selectedManifest = manifest ?? RuntimeManifest();
        var store = new InMemoryDocumentStore(selectedManifest, access);
        return new GroundworkDocumentStoreFixture(
            store,
            GroundworkPhysicalTestStores.Route(selectedManifest, _ => store));
    }

    private static StorageManifest RuntimeManifest() =>
        new RuntimeGroundworkStorageManifestSource()
            .CreateDeclarationAsync()
            .GetAwaiter()
            .GetResult()
            .Manifest;

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

#pragma warning disable GW0004 // IDocumentStore compatibility surface delegated by the decorator.
        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default) =>
            documents.QueryAsync(query, cancellationToken);

        public Task<DocumentQueryResult> QueryAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            documents.QueryAsync(query, cancellationToken);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            documents.FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            documents.AnyAsync(query, cancellationToken);
#pragma warning restore GW0004

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

internal sealed class RecordingBoundedDocumentStore(IBoundedDocumentStore inner) : IBoundedDocumentStore
{
    public List<DocumentQuery> Queries { get; } = [];
    public List<int> ReturnedDocumentCounts { get; } = [];

    public async Task<DocumentQueryResult> QueryAsync(
        DocumentQuery query,
        CancellationToken cancellationToken = default)
    {
        Queries.Add(query);
        var result = await inner.QueryAsync(query, cancellationToken);
        ReturnedDocumentCounts.Add(result.Documents.Count);
        return result;
    }

    public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        inner.CountAsync(query, cancellationToken);

    public Task<DocumentEnvelope?> FirstOrDefaultAsync(
        DocumentQuery query,
        CancellationToken cancellationToken = default) =>
        inner.FirstOrDefaultAsync(query, cancellationToken);

    public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        inner.AnyAsync(query, cancellationToken);
}

internal sealed class EmptyRecordingBoundedDocumentStore : IBoundedDocumentStore
{
    public List<DocumentQuery> Queries { get; } = [];

    public Task<DocumentQueryResult> QueryAsync(
        DocumentQuery query,
        CancellationToken cancellationToken = default)
    {
        Queries.Add(query);
        return Task.FromResult(new DocumentQueryResult([], 0));
    }

    public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(0L);

    public Task<DocumentEnvelope?> FirstOrDefaultAsync(
        DocumentQuery query,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<DocumentEnvelope?>(null);

    public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
