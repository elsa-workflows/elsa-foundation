using Elsa.Locking.Options;
using Medallion.Threading;
using Microsoft.Extensions.Options;

namespace Elsa.Locking.Adaptors
{
    /// <inheritdoc />
    public sealed class DistributedLockProviderAdaptor(IDistributedLockProvider distributedLockProvider, IOptions<DistributedLockingOptions> options) : Elsa.Locking.Core.IDistributedLockProvider
    {
        public async ValueTask<Elsa.Locking.Core.IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            var handle = await distributedLockProvider.AcquireLockAsync(name, timeout ?? options.Value.LockAcquisitionTimeout, cancellationToken);
            return new DistributedLockHandleAdaptor(handle);
        }

        /// <inheritdoc />
        public Elsa.Locking.Core.IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            var handle = distributedLockProvider.TryAcquireLock(name, timeout ?? options.Value.LockAcquisitionTimeout, cancellationToken);

            return handle is not null
                ? new DistributedLockHandleAdaptor(handle)
                : null;
        }

        public async ValueTask<Elsa.Locking.Core.IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            var handle = await distributedLockProvider.TryAcquireLockAsync(name, timeout ?? options.Value.LockAcquisitionTimeout, cancellationToken);

            return handle is not null
              ? new DistributedLockHandleAdaptor(handle)
              : null;
        }
    }    
}
