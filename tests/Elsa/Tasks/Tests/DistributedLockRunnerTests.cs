using Elsa.Locking.Core;

namespace Elsa.Tasks.Tests;

public sealed class DistributedLockRunnerTests
{
    [Fact]
    public async Task AcquiredHandle_RunsAction_DisposesHandle_AndReturnsTrue()
    {
        var handle = new RecordingHandle();
        var provider = new RecordingLockProvider(handle);
        var actionCalls = 0;

        var acquired = await provider.TryRunUnderDistributedLock(
            "startup:reconcile",
            TimeSpan.FromSeconds(1),
            _ =>
            {
                actionCalls++;
                Assert.Equal(0, handle.DisposeAsyncCount);
                return Task.CompletedTask;
            });

        Assert.True(acquired);
        Assert.Equal(1, actionCalls);
        Assert.Equal("startup:reconcile", provider.RequestedKey);
        Assert.Equal(TimeSpan.FromSeconds(1), provider.RequestedTimeout);
        Assert.Equal(1, handle.DisposeAsyncCount);
    }

    [Fact]
    public async Task MissingHandle_SkipsAction_AndReturnsFalse()
    {
        var provider = new RecordingLockProvider(handle: null);
        var actionCalls = 0;

        var acquired = await provider.TryRunUnderDistributedLock(
            "startup:reconcile",
            TimeSpan.FromMilliseconds(25),
            _ =>
            {
                actionCalls++;
                return Task.CompletedTask;
            });

        Assert.False(acquired);
        Assert.Equal(0, actionCalls);
    }

    [Fact]
    public async Task ActionFailure_DisposesHandle_AndPropagates()
    {
        var handle = new RecordingHandle();
        var provider = new RecordingLockProvider(handle);
        var failure = new InvalidOperationException("action failed");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.TryRunUnderDistributedLock(
            "startup:reconcile",
            TimeSpan.FromSeconds(1),
            _ => throw failure));

        Assert.Same(failure, thrown);
        Assert.Equal(1, handle.DisposeAsyncCount);
    }

    [Fact]
    public async Task CancellationDuringAcquire_Propagates()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var provider = new RecordingLockProvider(new RecordingHandle(), observeCancellation: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.TryRunUnderDistributedLock(
            "startup:reconcile",
            TimeSpan.FromSeconds(1),
            _ => Task.CompletedTask,
            cancelled.Token));
    }

    [Fact]
    public async Task CancellationDuringAction_DisposesHandle_AndPropagates()
    {
        var handle = new RecordingHandle();
        var provider = new RecordingLockProvider(handle);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.TryRunUnderDistributedLock(
            "startup:reconcile",
            TimeSpan.FromSeconds(1),
            ct =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            },
            cancelled.Token));

        Assert.Equal(1, handle.DisposeAsyncCount);
    }

    [Fact]
    public async Task NullProvider_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            ((IDistributedLockProvider)null!).TryRunUnderDistributedLock(
                "startup:reconcile",
                TimeSpan.FromSeconds(1),
                _ => Task.CompletedTask));
    }

    [Fact]
    public async Task NullAction_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            new RecordingLockProvider(new RecordingHandle()).TryRunUnderDistributedLock(
                "startup:reconcile",
                TimeSpan.FromSeconds(1),
                null!));
    }

    [Fact]
    public async Task MissingLockKey_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new RecordingLockProvider(new RecordingHandle()).TryRunUnderDistributedLock(
                " ",
                TimeSpan.FromSeconds(1),
                _ => Task.CompletedTask));
    }

    private sealed class RecordingLockProvider(RecordingHandle? handle, bool observeCancellation = false)
        : IDistributedLockProvider
    {
        public string? RequestedKey { get; private set; }
        public TimeSpan? RequestedTimeout { get; private set; }

        public IDistributedSynchronizationHandle? TryAcquireLock(
            string name,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(
            string name,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            if (observeCancellation)
                cancellationToken.ThrowIfCancellationRequested();

            RequestedKey = name;
            RequestedTimeout = timeout;
            return ValueTask.FromResult<IDistributedSynchronizationHandle?>(handle);
        }

        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(
            string name,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingHandle : IDistributedSynchronizationHandle
    {
        public int DisposeAsyncCount { get; private set; }

        public CancellationToken HandleLostToken => CancellationToken.None;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            DisposeAsyncCount++;
            return ValueTask.CompletedTask;
        }
    }
}
