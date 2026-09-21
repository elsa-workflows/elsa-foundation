namespace Elsa.Locking.Core;

/// <summary>
/// Runs one action if a distributed lock can be acquired, otherwise returns without throwing.
/// Lock keys and logging stay with the caller so distinct reconcilers keep distinct keys.
/// </summary>
public static class DistributedLockRunner
{
    /// <summary>Runs <paramref name="action"/> under <paramref name="lockKey"/> if the lock can be taken.</summary>
    /// <param name="lockKey">The lock's identity. Distinct callers must pass distinct keys, or they serialize behind one another.</param>
    /// <param name="timeout">How long to wait for the lock before giving up.</param>
    /// <param name="action">Runs only when the lock was taken, and always under it: the handle is released after it completes or throws.</param>
    /// <returns>
    /// <c>true</c> when the lock was taken and <paramref name="action"/> ran; <c>false</c> when another holder had it.
    /// <c>false</c> is an expected outcome, not a failure - the caller decides whether to log it, skip, or retry.
    /// </returns>
    public static async Task<bool> TryRunUnderDistributedLock(
        this IDistributedLockProvider distributedLockProvider,
        string lockKey,
        TimeSpan timeout,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(distributedLockProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(lockKey);
        ArgumentNullException.ThrowIfNull(action);

        await using var handle = await distributedLockProvider.TryAcquireLockAsync(lockKey, timeout, cancellationToken);
        if (handle is null)
            return false;

        await action(cancellationToken);
        return true;
    }
}
