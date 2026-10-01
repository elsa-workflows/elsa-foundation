using CShells;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Services;
using Elsa.Locking.Core;
using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using Elsa.Tasks.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Architecture;

/// <summary>
/// What <c>[SingleNodeTask]</c> means since #2192: one at a time, at shell start. A node waits for the lock, bounded by the
/// lock provider's acquisition timeout, and then runs the task, rather than skipping it because another node holds the lock
/// (the try-and-skip that IN-4 introduced). The key is the shell's and the task type's, losing the lock cancels the task as
/// a failure, and a node that is dormant for the task skips it before it touches the lock.
/// </summary>
public sealed class TaskExecutorSingleNodeTests
{
    private const string ShellName = "tenant-a";
    private const string SpyTaskKey = "elsa:single-node-task:tenant-a:Elsa.Architecture.TaskExecutorSingleNodeTests+SingleNodeSpyTask";

    private readonly List<string> _calls = [];
    private readonly FakeDistributedLockProvider _locks;
    private readonly FakeDormancyCheck _dormancy;
    private readonly SingleNodeSpyTask _task = new();

    public TaskExecutorSingleNodeTests()
    {
        _locks = new FakeDistributedLockProvider(_calls);
        _dormancy = new FakeDormancyCheck(_calls);
    }

    [Fact]
    public async Task Runs_at_once_and_releases_the_lock_when_it_is_free()
    {
        await Executor().ExecuteTaskAsync(_task, CancellationToken.None);

        Assert.True(_task.Executed);
        Assert.Equal((SpyTaskKey, (TimeSpan?)TimeSpan.Zero), Assert.Single(_locks.Attempts));
        Assert.True(Assert.Single(_locks.Handles).Released);
    }

    [Fact]
    public async Task Waits_for_another_holder_and_then_runs_instead_of_skipping()
    {
        // Held when first asked, free once the wait bounded by the provider's own acquisition timeout (no timeout) ends.
        _locks.Acquire = (_, timeout) => timeout == TimeSpan.Zero ? null : new FakeHandle();

        await Executor().ExecuteTaskAsync(_task, CancellationToken.None);

        Assert.True(_task.Executed);
        Assert.Equal(new (string, TimeSpan?)[] { (SpyTaskKey, TimeSpan.Zero), (SpyTaskKey, null) }, _locks.Attempts);
        Assert.Equal(0, _locks.BlockingAcquireCallCount);
    }

    [Fact]
    public async Task Fails_naming_the_lock_when_the_bounded_wait_runs_out()
    {
        _locks.Acquire = (_, _) => null;

        var failure = await Assert.ThrowsAsync<TimeoutException>(() => Executor().ExecuteTaskAsync(_task, CancellationToken.None));

        Assert.False(_task.Executed);
        Assert.Contains(SpyTaskKey, failure.Message);
        Assert.Equal(2, _locks.Attempts.Count);
    }

    [Fact]
    public async Task The_key_is_the_shells_so_another_shell_of_the_process_does_not_wait()
    {
        _locks.Acquire = (name, _) => name == SpyTaskKey ? null : new FakeHandle();

        await Executor(shellName: "tenant-b").ExecuteTaskAsync(_task, CancellationToken.None);

        Assert.True(_task.Executed);
        Assert.Equal(
            ("elsa:single-node-task:tenant-b:Elsa.Architecture.TaskExecutorSingleNodeTests+SingleNodeSpyTask", (TimeSpan?)TimeSpan.Zero),
            Assert.Single(_locks.Attempts));
    }

    [Fact]
    public async Task Losing_the_lock_cancels_the_task_and_fails_it_rather_than_reporting_a_cancellation()
    {
        var handle = new FakeHandle(canBeLost: true);
        _locks.Acquire = (_, _) => handle;
        var task = new SingleNodeAwaitingTask(handle.Lose);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Executor().ExecuteTaskAsync(task, CancellationToken.None));

        Assert.True(task.ObservedCancellation);
        Assert.IsAssignableFrom<OperationCanceledException>(failure.InnerException);
        Assert.Contains(SpyTaskKey.Replace(nameof(SingleNodeSpyTask), nameof(SingleNodeAwaitingTask)), failure.Message);
        Assert.True(handle.Released);
    }

    [Fact]
    public async Task A_release_that_fails_after_the_lock_was_lost_does_not_replace_the_lost_lock_failure()
    {
        // As a database provider's release does once the connection that held the lock is gone.
        var handle = new FakeHandle(canBeLost: true, failsToRelease: true);
        _locks.Acquire = (_, _) => handle;

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Executor().ExecuteTaskAsync(new SingleNodeAwaitingTask(handle.Lose), CancellationToken.None));

        Assert.IsAssignableFrom<OperationCanceledException>(failure.InnerException);
        Assert.True(handle.Released);
    }

    [Fact]
    public async Task A_release_that_fails_after_the_task_completed_does_not_fail_it()
    {
        var handle = new FakeHandle(failsToRelease: true);
        _locks.Acquire = (_, _) => handle;

        await Executor().ExecuteTaskAsync(_task, CancellationToken.None);

        Assert.True(_task.Executed);
        Assert.True(handle.Released);
    }

    [Fact]
    public async Task Cancelling_the_shell_while_the_lock_is_held_stays_a_cancellation()
    {
        using var shutdown = new CancellationTokenSource();
        var handle = new FakeHandle(canBeLost: true);
        _locks.Acquire = (_, _) => handle;
        var task = new SingleNodeAwaitingTask(shutdown.Cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Executor().ExecuteTaskAsync(task, shutdown.Token));

        Assert.True(task.ObservedCancellation);
        Assert.False(handle.LostToken.IsCancellationRequested);
    }

    [Fact]
    public async Task A_task_that_finishes_after_losing_its_lock_is_not_failed()
    {
        var handle = new FakeHandle(canBeLost: true);
        _locks.Acquire = (_, _) => handle;
        var task = new SingleNodeSpyTask(onExecute: handle.Lose);

        await Executor().ExecuteTaskAsync(task, CancellationToken.None);

        Assert.True(task.Executed);
    }

    [Fact]
    public async Task A_dormant_node_skips_the_task_without_touching_the_lock()
    {
        _dormancy.Dormant = true;
        var task = new DormancyAwareSpyTask();

        await Executor().ExecuteTaskAsync(task, CancellationToken.None);

        Assert.False(task.Executed);
        Assert.Equal(["dormancy"], _calls);
        Assert.Equal([new SchemaVersionRequirement("Tests.Family", "2.0.0")], _dormancy.Asked);
    }

    [Fact]
    public async Task Dormancy_is_asked_before_the_lock_is_taken()
    {
        var task = new DormancyAwareSpyTask();

        await Executor().ExecuteTaskAsync(task, CancellationToken.None);

        Assert.True(task.Executed);
        Assert.Equal(["dormancy", "lock:elsa:single-node-task:tenant-a:Elsa.Architecture.TaskExecutorSingleNodeTests+DormancyAwareSpyTask"], _calls);
    }

    [Fact]
    public async Task A_declared_requirement_is_never_met_without_a_dormancy_check()
    {
        var task = new DormancyAwareSpyTask();

        await new TaskExecutor(_locks, NullLogger<TaskExecutor>.Instance, new ShellSettings(ShellName)).ExecuteTaskAsync(task, CancellationToken.None);

        Assert.False(task.Executed);
        Assert.Empty(_locks.Attempts);
    }

    [Fact]
    public async Task Propagates_a_lock_provider_failure_rather_than_swallowing_it()
    {
        _locks.Acquire = (_, _) => throw new InvalidOperationException("lock backend down");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Executor().ExecuteTaskAsync(_task, CancellationToken.None));

        Assert.Equal("lock backend down", failure.Message);
        Assert.False(_task.Executed);
    }

    [Fact]
    public async Task A_task_without_the_attribute_runs_without_touching_the_lock()
    {
        var task = new PlainSpyTask();

        await Executor().ExecuteTaskAsync(task, CancellationToken.None);

        Assert.True(task.Executed);
        Assert.Empty(_locks.Attempts);
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task A_background_start_waits_and_then_runs()
    {
        _locks.Acquire = (_, timeout) => timeout == TimeSpan.Zero ? null : new FakeHandle();
        var task = new SingleNodeBackgroundSpyTask();

        await ((IBackgroundTaskStarter)Executor()).StartAsync(task, CancellationToken.None);

        Assert.True(task.Started);
        Assert.Equal(0, _locks.BlockingAcquireCallCount);
    }

    private TaskExecutor Executor(string shellName = ShellName) =>
        new(_locks, NullLogger<TaskExecutor>.Instance, new ShellSettings(shellName), _dormancy);

    [SingleNodeTask]
    private sealed class SingleNodeSpyTask(Action? onExecute = null) : ITask
    {
        public bool Executed { get; private set; }

        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            onExecute?.Invoke();
            Executed = true;
            return Task.CompletedTask;
        }
    }

    /// <summary>Runs until its token is cancelled, doing <paramref name="whileRunning"/> once it waits, and reports that it saw the cancellation.</summary>
    [SingleNodeTask]
    private sealed class SingleNodeAwaitingTask(Action whileRunning) : ITask
    {
        public bool ObservedCancellation { get; private set; }

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            var waiting = Task.Delay(Timeout.Infinite, cancellationToken);
            whileRunning();
            try
            {
                await waiting;
            }
            catch (OperationCanceledException)
            {
                ObservedCancellation = true;
                throw;
            }
        }
    }

    [SingleNodeTask]
    [RequiresSchemaVersion("Tests.Family", "2.0.0")]
    private sealed class DormancyAwareSpyTask : ITask
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

    private sealed class FakeDistributedLockProvider(List<string> calls) : IDistributedLockProvider
    {
        public Func<string, TimeSpan?, IDistributedSynchronizationHandle?> Acquire { get; set; } = (_, _) => new FakeHandle();
        public List<(string Name, TimeSpan? Timeout)> Attempts { get; } = [];
        public List<FakeHandle> Handles { get; } = [];
        public int BlockingAcquireCallCount { get; private set; }

        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The executor acquires asynchronously.");

        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            calls.Add($"lock:{name}");
            Attempts.Add((name, timeout));
            var handle = Acquire(name, timeout);
            if (handle is FakeHandle fake)
                Handles.Add(fake);
            return ValueTask.FromResult(handle);
        }

        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            // The blocking, throw-on-timeout acquisition: the executor waits through TryAcquireLockAsync, so that it can
            // name the lock when the wait runs out.
            BlockingAcquireCallCount++;
            throw new InvalidOperationException("Blocking AcquireLockAsync must not be used for single-node task acquisition.");
        }
    }

    private sealed class FakeHandle(bool canBeLost = false, bool failsToRelease = false) : IDistributedSynchronizationHandle
    {
        private readonly CancellationTokenSource _lost = new();

        public bool Released { get; private set; }
        public CancellationToken LostToken => _lost.Token;
        public CancellationToken HandleLostToken => canBeLost ? _lost.Token : CancellationToken.None;

        public void Lose() => _lost.Cancel();

        public void Dispose()
        {
            Released = true;
            if (failsToRelease)
                throw new InvalidOperationException("Connection is not open.");
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeDormancyCheck(List<string> calls) : ISchemaDormancyCheck
    {
        public bool Dormant { get; set; }
        public List<SchemaVersionRequirement> Asked { get; } = [];

        public ValueTask<SchemaAvailability> EvaluateAsync(IEnumerable<SchemaVersionRequirement> requirements, CancellationToken cancellationToken = default)
        {
            calls.Add("dormancy");
            var asked = requirements.ToArray();
            Asked.AddRange(asked);
            return ValueTask.FromResult(Dormant ? SchemaDormancyRule.Evaluate(asked, _ => null) : SchemaAvailability.Available);
        }

        public SchemaAvailability Evaluate(IEnumerable<SchemaVersionRequirement> requirements) => throw new NotSupportedException();
        public ValueTask EnsureAvailableAsync(IEnumerable<SchemaVersionRequirement> requirements, string? featureId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IReadOnlyList<SchemaFamilyObservation> Observe() => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<SchemaFamilyObservation>> ReadStatusAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
