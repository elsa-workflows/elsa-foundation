namespace Elsa.Locking.Core;

/// <summary>
/// Provides distributed locks that can be used to coordinate access to shared resources across multiple nodes in a distributed system. Implementations of this interface should provide a way to acquire and release locks in a distributed manner, ensuring that only one node can hold a lock on a given resource at any time. This is essential for scenarios where multiple instances of an application are running and need to synchronize access to shared resources such as databases, files, or external services.
/// </summary>
public interface IDistributedLockProvider
{
    /// <summary>
    /// Tries to acquire a distributed lock. When no timeout is specified, the default configued lock acquisition timeout will be used. When the lock is successfully acquired, a handle is returned that can be used to release the lock. If the lock cannot be acquired within the specified timeout, null is returned.
    /// </summary>
    /// <param name="name"></param>
    /// <param name="timeout"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tries to acquire a distributed lock asynchronously. When no timeout is specified, the default configued lock acquisition timeout will be used. When the lock is successfully acquired, a handle is returned that can be used to release the lock. If the lock cannot be acquired within the specified timeout, null is returned.
    /// </summary>
    /// <param name="name"></param>
    /// <param name="timeout"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquires a distributed lock asynchronously. When no timeout is specified, the default configued lock acquisition timeout will be used. When the lock is successfully acquired, a handle is returned that can be used to release the lock. If the lock cannot be acquired within the specified timeout, a TimeoutException is thrown.
    /// </summary>
    /// <param name="name"></param>
    /// <param name="timeout"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
}