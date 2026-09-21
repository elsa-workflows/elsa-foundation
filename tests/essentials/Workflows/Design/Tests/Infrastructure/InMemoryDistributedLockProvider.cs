using Elsa.Locking.Core;
using System.Collections.Concurrent;

namespace Elsa.Workflows.Design.Tests.Infrastructure;

/// <summary>
/// In-process <see cref="IDistributedLockProvider"/> backed by a <see cref="SemaphoreSlim"/>
/// per lock name. Test-only; demonstrates per-name serialisation + cross-name parallelism
/// for SC-016. Production deployments use the FileSystem/Redis adapters in
/// <c>Elsa.Locking.FileSystem</c> / future <c>Elsa.Locking.Redis</c>.
/// </summary>
public sealed class InMemoryDistributedLockProvider : IDistributedLockProvider
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();

    /// <summary>
    /// Per-name count of successful lock acquisitions. Lets tests assert the
    /// "lock-once-per-call against <c>workflow-draft:{DraftId}</c>" contract (SC-003).
    /// </summary>
    public ConcurrentDictionary<string, int> AcquireCounts { get; } = new();

    private SemaphoreSlim GetSemaphore(string name) => _semaphores.GetOrAdd(name, _ => new SemaphoreSlim(1, 1));

    private void CountAcquire(string name) => AcquireCounts.AddOrUpdate(name, 1, (_, n) => n + 1);

    public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var sem = GetSemaphore(name);
        var acquired = sem.Wait(timeout ?? TimeSpan.Zero, cancellationToken);
        if (acquired)
            CountAcquire(name);
        return acquired ? new Handle(sem) : null;
    }

    public async ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var sem = GetSemaphore(name);
        var acquired = await sem.WaitAsync(timeout ?? TimeSpan.Zero, cancellationToken);
        if (acquired)
            CountAcquire(name);
        return acquired ? new Handle(sem) : null;
    }

    public async ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var sem = GetSemaphore(name);
        await sem.WaitAsync(cancellationToken);
        CountAcquire(name);
        return new Handle(sem);
    }

    private sealed class Handle(SemaphoreSlim semaphore) : IDistributedSynchronizationHandle
    {
        private int _released;

        public CancellationToken HandleLostToken => CancellationToken.None;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                semaphore.Release();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
