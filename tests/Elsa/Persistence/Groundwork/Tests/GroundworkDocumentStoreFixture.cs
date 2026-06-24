using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
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
        var handle = SqliteDocumentStoreFactory
            .CreateAsync(connectionString, manifest ?? ElsaRuntimeStorageManifest.Create(), SqliteProvider)
            .GetAwaiter()
            .GetResult();

        return new GroundworkDocumentStoreFixture(handle.Store, handle);
    }

    public async ValueTask DisposeAsync()
    {
        if (owner is not null)
            await owner.DisposeAsync();
        else if (DocumentStore is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
    }
}
