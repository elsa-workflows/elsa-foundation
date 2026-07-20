using Elsa.Locking.Core;

namespace Elsa.Persistence.Groundwork.DesignConformance.Tests;

/// <summary>
/// Stateless, immediately acquired lock used by provider fixtures whose isolation comes from a
/// dedicated database. Production lock orchestration remains exercised by the composed commands.
/// </summary>
public sealed class ImmediateDesignContractLockProvider : IDistributedLockProvider
{
    public IDistributedSynchronizationHandle? TryAcquireLock(
        string name,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) => new Handle();

    public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(
        string name,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IDistributedSynchronizationHandle?>(new Handle());

    public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(
        string name,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IDistributedSynchronizationHandle>(new Handle());

    private sealed class Handle : IDistributedSynchronizationHandle
    {
        public CancellationToken HandleLostToken => CancellationToken.None;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
