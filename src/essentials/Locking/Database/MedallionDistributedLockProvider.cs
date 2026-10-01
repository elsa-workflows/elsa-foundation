using Medallion.Threading;
using IDistributedLockProvider = Elsa.Locking.Core.IDistributedLockProvider;
using IDistributedSynchronizationHandle = Elsa.Locking.Core.IDistributedSynchronizationHandle;

namespace Elsa.Locking.Database;

/// <summary>
/// Adapts one of Medallion's database lock providers to <see cref="IDistributedLockProvider"/>, filling in the feature's
/// acquisition timeout when a caller passes none. The same shape as <c>Elsa.Locking.FileSystem</c>'s adaptor, kept
/// separate rather than shared so neither provider package depends on the other.
/// </summary>
public sealed class MedallionDistributedLockProvider(Medallion.Threading.IDistributedLockProvider inner, TimeSpan defaultTimeout) : IDistributedLockProvider
{
    public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        Wrap(inner.TryAcquireLock(name, timeout ?? defaultTimeout, cancellationToken));

    public async ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        Wrap(await inner.TryAcquireLockAsync(name, timeout ?? defaultTimeout, cancellationToken));

    public async ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        new Handle(await inner.AcquireLockAsync(name, timeout ?? defaultTimeout, cancellationToken));

    private static IDistributedSynchronizationHandle? Wrap(Medallion.Threading.IDistributedSynchronizationHandle? handle) =>
        handle is null ? null : new Handle(handle);

    private sealed class Handle(Medallion.Threading.IDistributedSynchronizationHandle inner) : IDistributedSynchronizationHandle
    {
        public CancellationToken HandleLostToken => inner.HandleLostToken;

        public void Dispose() => inner.Dispose();

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
