using Elsa.Locking.Core;

namespace Elsa.Workflows.Runtime.Distributed.Tests.Placement;

/// <summary>The placement pump takes no single-node lock; the task executor only needs one to exist.</summary>
internal sealed class NoLocks : IDistributedLockProvider
{
    public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
