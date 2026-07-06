using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork;

/// <summary>
/// Provider-neutral singleton that holds the one Groundwork <see cref="IDocumentStore"/> after it has been
/// materialized at host startup. A provider-specific document-store initializer (registered as both an
/// <see cref="Microsoft.Extensions.Hosting.IHostedService"/> and a CShells <c>IShellInitializer</c>) awaits the
/// async store creation exactly once and calls <see cref="Set"/>; <see cref="IDocumentStore"/> is then registered
/// to resolve from <see cref="Store"/>, so consumers keep resolving a fully-initialized singleton without any
/// synchronous block on the resolving thread.
/// </summary>
/// <remarks>
/// The holder owns the async-disposable store handle: DI disposes this singleton, which disposes the handle and
/// so the underlying connection. Resolving <see cref="IDocumentStore"/> before the initializer has run throws a
/// descriptive <see cref="InvalidOperationException"/> rather than silently blocking — a bare
/// <see cref="IServiceProvider"/> with no host lifecycle must drive the initializer explicitly first.
/// </remarks>
public sealed class GroundworkDocumentStoreHolder : IAsyncDisposable
{
    private IDocumentStore? _store;
    private IAsyncDisposable? _handle;

    /// <summary>Whether the store has been initialized. Initializers guard on this to stay idempotent.</summary>
    public bool IsInitialized => _store is not null;

    /// <summary>The initialized document store. Throws until the startup initializer has populated it.</summary>
    public IDocumentStore Store => _store ?? throw new InvalidOperationException(
        "The Groundwork document store has not been initialized yet. It is created once at host startup by a " +
        "hosted service / CShells shell initializer; ensure that has run before resolving IDocumentStore. A bare " +
        "ServiceProvider with no host lifecycle must invoke the store initializer's InitializeAsync first.");

    /// <summary>
    /// Populates the holder with the materialized store and its owning handle. Called once by the startup
    /// initializer; subsequent calls are ignored so re-running the hook (e.g. both lifecycle hooks) is safe.
    /// </summary>
    public void Set(IDocumentStore store, IAsyncDisposable handle)
    {
        if (_store is not null)
            return;

        _store = store;
        _handle = handle;
    }

    public ValueTask DisposeAsync() => _handle?.DisposeAsync() ?? ValueTask.CompletedTask;
}
