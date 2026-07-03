using Elsa.Locking.Core;
using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using Elsa.Tasks.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Architecture;

/// <summary>
/// IN-4 (elsa-4-review-remediation, W15): a <c>[SingleNodeTask]</c> must acquire its distributed lock with a
/// non-blocking try-acquire so a secondary node whose peer already holds the lock skips immediately, rather than
/// blocking for the full lock-acquisition timeout (10 minutes by default) and then crashing startup with a
/// <see cref="TimeoutException"/>. These tests pin that contract on <see cref="TaskExecutor"/>.
/// </summary>
public sealed class TaskExecutorSingleNodeTests
{
    [Fact]
    public async Task SingleNodeTask_RunsAction_WhenLockIsAvailable()
    {
        var lockProvider = new FakeDistributedLockProvider { TryAcquireResult = new FakeHandle() };
        var executor = new TaskExecutor(lockProvider, NullLogger<TaskExecutor>.Instance);
        var task = new SingleNodeSpyTask();

        await executor.ExecuteTaskAsync(task, CancellationToken.None);

        Assert.True(task.Executed);
        Assert.Equal(1, lockProvider.TryAcquireCallCount);
        Assert.Equal(0, lockProvider.BlockingAcquireCallCount);
    }

    [Fact]
    public async Task SingleNodeTask_SkipsAction_WhenLockHeldByAnotherNode_WithoutBlockingAcquire()
    {
        // A null try-acquire result means another node owns the lock: the task is skipped, no exception, and the
        // blocking AcquireLockAsync (the 10-minute-then-throw path) is never called.
        var lockProvider = new FakeDistributedLockProvider { TryAcquireResult = null };
        var executor = new TaskExecutor(lockProvider, NullLogger<TaskExecutor>.Instance);
        var task = new SingleNodeSpyTask();

        await executor.ExecuteTaskAsync(task, CancellationToken.None);

        Assert.False(task.Executed);
        Assert.Equal(1, lockProvider.TryAcquireCallCount);
        Assert.Equal(0, lockProvider.BlockingAcquireCallCount);
    }

    [Fact]
    public async Task SingleNodeTask_UsesNonBlockingZeroTimeout()
    {
        var lockProvider = new FakeDistributedLockProvider { TryAcquireResult = null };
        var executor = new TaskExecutor(lockProvider, NullLogger<TaskExecutor>.Instance);

        await executor.ExecuteTaskAsync(new SingleNodeSpyTask(), CancellationToken.None);

        // A zero timeout is what guarantees a secondary node cannot block for the default 10-minute window.
        Assert.Equal(TimeSpan.Zero, lockProvider.LastTryAcquireTimeout);
    }

    [Fact]
    public async Task SingleNodeTask_PropagatesLockProviderException_RatherThanSwallowingIt()
    {
        var lockProvider = new FakeDistributedLockProvider { TryAcquireException = new InvalidOperationException("lock backend down") };
        var executor = new TaskExecutor(lockProvider, NullLogger<TaskExecutor>.Instance);
        var task = new SingleNodeSpyTask();

        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteTaskAsync(task, CancellationToken.None));

        Assert.False(task.Executed);
    }

    [Fact]
    public async Task NonSingleNodeTask_RunsAction_WithoutTouchingLockProvider()
    {
        var lockProvider = new FakeDistributedLockProvider { TryAcquireResult = new FakeHandle() };
        var executor = new TaskExecutor(lockProvider, NullLogger<TaskExecutor>.Instance);
        var task = new PlainSpyTask();

        await executor.ExecuteTaskAsync(task, CancellationToken.None);

        Assert.True(task.Executed);
        Assert.Equal(0, lockProvider.TryAcquireCallCount);
        Assert.Equal(0, lockProvider.BlockingAcquireCallCount);
    }

    [Fact]
    public async Task BackgroundStart_ForSingleNodeTask_SkipsWhenLockHeld()
    {
        var lockProvider = new FakeDistributedLockProvider { TryAcquireResult = null };
        var executor = new TaskExecutor(lockProvider, NullLogger<TaskExecutor>.Instance);
        var task = new SingleNodeBackgroundSpyTask();

        await ((IBackgroundTaskStarter)executor).StartAsync(task, CancellationToken.None);

        Assert.False(task.Started);
        Assert.Equal(0, lockProvider.BlockingAcquireCallCount);
    }

    [SingleNodeTask]
    private sealed class SingleNodeSpyTask : ITask
    {
        public bool Executed { get; private set; }

        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            Executed = true;
            return Task.CompletedTask;
        }
    }

    [SingleNodeTask]
    private sealed class SingleNodeBackgroundSpyTask : IBackgroundTask
    {
        public bool Started { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Started = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class PlainSpyTask : ITask
    {
        public bool Executed { get; private set; }

        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            Executed = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDistributedLockProvider : IDistributedLockProvider
    {
        public IDistributedSynchronizationHandle? TryAcquireResult { get; init; }
        public Exception? TryAcquireException { get; init; }
        public int TryAcquireCallCount { get; private set; }
        public int BlockingAcquireCallCount { get; private set; }
        public TimeSpan? LastTryAcquireTimeout { get; private set; }

        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            TryAcquireCallCount++;
            LastTryAcquireTimeout = timeout;
            if (TryAcquireException is not null)
                throw TryAcquireException;
            return TryAcquireResult;
        }

        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            TryAcquireCallCount++;
            LastTryAcquireTimeout = timeout;
            if (TryAcquireException is not null)
                throw TryAcquireException;
            return ValueTask.FromResult(TryAcquireResult);
        }

        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            // The blocking, throw-on-timeout path IN-4 replaces. If it is ever called for a single-node task, the
            // tests fail via BlockingAcquireCallCount; throwing here would otherwise mask that regression.
            BlockingAcquireCallCount++;
            throw new InvalidOperationException("Blocking AcquireLockAsync must not be used for single-node task acquisition.");
        }
    }

    private sealed class FakeHandle : IDistributedSynchronizationHandle
    {
        public CancellationToken HandleLostToken => CancellationToken.None;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
