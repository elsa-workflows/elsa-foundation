using Groundwork.Documents.Store;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Sqlite;

/// <summary>Owns the Publishing lane's document store without replacing the host's Runtime document store.</summary>
public sealed class PublishingGroundworkDocumentStoreHolder : IAsyncDisposable
{
    private IDocumentStore? _store;

    public bool IsInitialized => _store is not null;

    public IDocumentStore Store => _store ?? throw new InvalidOperationException(
        "The Publishing Groundwork document store has not been initialized. Ensure the Publishing SQLite " +
        "shell initializer runs before resolving publication stores.");

    public void Set(IDocumentStore store) => _store ??= store;

    public ValueTask DisposeAsync() => _store is IAsyncDisposable disposable
        ? disposable.DisposeAsync()
        : ValueTask.CompletedTask;
}
