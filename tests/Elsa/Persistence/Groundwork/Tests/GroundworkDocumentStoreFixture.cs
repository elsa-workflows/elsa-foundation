using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Sqlite.Documents;

namespace Elsa.Persistence.Groundwork.Tests;

internal sealed class GroundworkDocumentStoreFixture(IDocumentStore documentStore, IAsyncDisposable? owner = null) : IAsyncDisposable
{
    private static readonly ProviderIdentity SqliteProvider = new("groundwork-sqlite", "1.0.0");

    public IDocumentStore DocumentStore { get; } = documentStore;

    public static GroundworkDocumentStoreFixture Create(string provider, StorageManifest? manifest = null) => provider switch
    {
        "sqlite" => CreateSqlite("Data Source=:memory:", manifest),
        "memory" => new GroundworkDocumentStoreFixture(new InMemoryDocumentStore(manifest ?? ElsaRuntimeStorageManifest.Create())),
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

        var store = SqliteDocumentStoreFactory
            .CreateAsync(
                connectionString,
                manifest ?? ElsaRuntimeStorageManifest.Create(),
                SqliteProvider,
                DocumentStoreAccess.Global)
            .GetAwaiter()
            .GetResult();

        return new GroundworkDocumentStoreFixture(store, database);
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
}
