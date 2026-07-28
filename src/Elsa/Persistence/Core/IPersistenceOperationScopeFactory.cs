using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Core;

/// <summary>
/// Creates a dependency-injection scope whose ordinary persistence context is bound to one explicit partition.
/// </summary>
public interface IPersistenceOperationScopeFactory
{
    ValueTask<PersistenceOperationScope> CreateAsync(
        PersistenceScope persistenceScope,
        CancellationToken cancellationToken = default);
}

public sealed class PersistenceOperationScope : IAsyncDisposable
{
    private AsyncServiceScope _scope;
    private int _disposed;

    internal PersistenceOperationScope(AsyncServiceScope scope) => _scope = scope;

    public IServiceProvider ServiceProvider => Volatile.Read(ref _disposed) == 0
        ? _scope.ServiceProvider
        : throw new ObjectDisposedException(nameof(PersistenceOperationScope));

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _scope.DisposeAsync();
    }
}
