namespace Elsa.Locking.Core;

/// <summary>
/// Runs one action if a distributed lock can be acquired, otherwise returns without throwing.
/// Lock keys and logging stay with the caller so distinct reconcilers keep distinct keys.
/// </summary>
public static class DistributedLockRunner
{
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
