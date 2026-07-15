using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork;

/// <summary>
/// Provider-neutral singleton that holds the one Groundwork <see cref="IDocumentStore"/> after it has been
/// materialized at host startup. A provider-specific document-store initializer (registered as both an
/// <see cref="Microsoft.Extensions.Hosting.IHostedService"/> and a CShells <c>IShellInitializer</c>) awaits the
/// async store creation exactly once and calls <see cref="TrySet"/>; <see cref="IDocumentStore"/> is then registered
/// to resolve from <see cref="Store"/>, so consumers keep resolving a fully-initialized singleton without any
/// synchronous block on the resolving thread.
/// </summary>
/// <remarks>
/// When a provider supplies an owning async-disposable handle, the holder owns it and DI disposal releases it.
/// Stateless providers need no owning handle. Resolving <see cref="IDocumentStore"/> before the initializer has run throws a
/// descriptive <see cref="InvalidOperationException"/> rather than silently blocking — a bare
/// <see cref="IServiceProvider"/> with no host lifecycle must drive the initializer explicitly first.
/// </remarks>
public sealed class GroundworkDocumentStoreHolder : IAsyncDisposable
{
    private readonly Lock _gate = new();
    private Publication? _publication;
    private bool _disposed;

    /// <summary>Whether the store has been initialized. Initializers guard on this to stay idempotent.</summary>
    public bool IsInitialized => Volatile.Read(ref _publication) is not null;

    /// <summary>The initialized document store. Throws until the startup initializer has populated it.</summary>
    public IDocumentStore Store => Volatile.Read(ref _publication)?.Store ?? throw new InvalidOperationException(
        "The Groundwork document store has not been initialized yet. It is created once at host startup by a " +
        "hosted service / CShells shell initializer; ensure that has run before resolving IDocumentStore. A bare " +
        "ServiceProvider with no host lifecycle must invoke the store initializer's InitializeAsync first.");

    /// <summary>The bounded-query runtime compiled from the same admitted physical target as <see cref="Store"/>.</summary>
    public IBoundedDocumentStore BoundedStore => Volatile.Read(ref _publication)?.BoundedStore ?? throw new InvalidOperationException(
        "The Groundwork bounded document store has not been initialized yet. Ensure the provider initializer " +
        "has admitted the selected physical target before resolving IBoundedDocumentStore.");

    /// <summary>
    /// Atomically publishes the materialized store, bounded-query runtime and owning handle. Exactly one
    /// caller wins; a losing caller remains responsible for disposing any handle it attempted to publish.
    /// </summary>
    public bool TrySet(
        IDocumentStore store,
        IBoundedDocumentStore boundedStore,
        IAsyncDisposable? handle = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(boundedStore);
        lock (_gate)
        {
            if (_disposed || _publication is not null)
                return false;

            Volatile.Write(ref _publication, new Publication(store, boundedStore, handle));
            return true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        IAsyncDisposable? handle;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            handle = _publication?.Handle;
        }

        if (handle is not null)
            await handle.DisposeAsync();
    }

    private sealed record Publication(
        IDocumentStore Store,
        IBoundedDocumentStore BoundedStore,
        IAsyncDisposable? Handle);
}
