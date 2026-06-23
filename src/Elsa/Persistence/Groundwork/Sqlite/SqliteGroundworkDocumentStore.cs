using Elsa.Persistence.Groundwork;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Groundwork.Sqlite.Documents;
using Groundwork.Sqlite.Materialization;
using Microsoft.Data.Sqlite;

namespace Elsa.Persistence.Groundwork.Sqlite;

/// <summary>
/// SQLite-backed <see cref="IDocumentStore"/> for the Groundwork runtime bridge. Owns a single
/// shared SQLite connection for the store lifetime (so <c>:memory:</c> databases survive between
/// operations) and materializes the runtime manifest exactly once, lazily, on first use.
/// </summary>
public sealed class SqliteGroundworkDocumentStore(string connectionString, StorageManifest manifest)
    : IDocumentStore, IAsyncDisposable
{
    private static readonly ProviderIdentity Provider = new("groundwork-sqlite", "1.0.0");

    private readonly SemaphoreSlim _gate = new(1, 1);
    private SqliteConnection? _connection;
    private SqliteDocumentStore? _inner;

    public async Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default) =>
        await (await GetStoreAsync(cancellationToken)).SaveAsync(request, cancellationToken);

    public async Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default) =>
        await (await GetStoreAsync(cancellationToken)).LoadAsync(documentKind, id, cancellationToken);

    public async Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default) =>
        await (await GetStoreAsync(cancellationToken)).DeleteAsync(request, cancellationToken);

    public async Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default) =>
        await (await GetStoreAsync(cancellationToken)).QueryAsync(query, cancellationToken);

    public async Task<DocumentQueryResult> QueryAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
        await (await GetStoreAsync(cancellationToken)).QueryAsync(query, cancellationToken);

    public async Task<DocumentEnvelope?> FirstOrDefaultAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
        await (await GetStoreAsync(cancellationToken)).FirstOrDefaultAsync(query, cancellationToken);

    public async Task<bool> AnyAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
        await (await GetStoreAsync(cancellationToken)).AnyAsync(query, cancellationToken);

    // SQLite is a relational provider: a unit of work spans documents in one DbTransaction, so the
    // boundary is always CrossUnitAtomic. The property is synchronous, so it reports the provider's
    // invariant rather than materializing the inner store on access.
    public TransactionBoundary TransactionBoundary => TransactionBoundary.CrossUnitAtomic;

    public async Task<IDocumentUnitOfWork> BeginAsync(DocumentCommitScope scope, CancellationToken cancellationToken = default) =>
        await (await GetStoreAsync(cancellationToken)).BeginAsync(scope, cancellationToken);

    private async ValueTask<SqliteDocumentStore> GetStoreAsync(CancellationToken cancellationToken)
    {
        if (_inner is not null)
            return _inner;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_inner is null)
            {
                var connection = new SqliteConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                await new SqliteGroundworkMaterializer(connection).MaterializeAsync(manifest, Provider, cancellationToken);
                _connection = connection;
                _inner = new SqliteDocumentStore(connection, manifest);
            }
        }
        finally
        {
            _gate.Release();
        }

        return _inner;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();

        _gate.Dispose();
    }
}
