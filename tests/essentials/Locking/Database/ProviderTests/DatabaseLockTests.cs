using CShells;
using Elsa.Locking.Core;
using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using Elsa.Tasks.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Locking.Database.ProviderTests;

/// <summary>
/// The database lock on a real engine (#2192). Each "node" is a lock provider of its own, composed through
/// <see cref="DatabaseLockingFeature"/> with its own connections, as two processes sharing one database would be.
/// </summary>
public abstract class DatabaseLockTests(LockDatabase database) : IAsyncDisposable
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);
    private readonly List<ServiceProvider> _nodes = [];

    [SkippableFact]
    public async Task Two_nodes_exclude_each_other_and_the_second_takes_the_lock_once_it_is_released()
    {
        Skip.If(database.SkipReason is not null, database.SkipReason);
        var key = $"exclusion-{Guid.NewGuid():N}";
        var first = Node();
        var second = Node();

        var held = await first.TryAcquireLockAsync(key, TimeSpan.Zero);
        Assert.NotNull(held);
        Assert.Null(await second.TryAcquireLockAsync(key, TimeSpan.Zero));

        await held.DisposeAsync();
        await using var taken = await second.TryAcquireLockAsync(key, Bound);
        Assert.NotNull(taken);
    }

    [SkippableFact]
    public async Task A_single_node_task_waits_for_the_other_node_and_then_runs_one_at_a_time()
    {
        Skip.If(database.SkipReason is not null, database.SkipReason);
        var probe = new Probe();
        var first = new GatedSingleNodeTask(probe);
        var second = new GatedSingleNodeTask(probe);
        second.Release.SetResult();

        var firstRun = Executor("default").ExecuteTaskAsync(first, CancellationToken.None);
        await first.Started.Task.WaitAsync(Bound);
        var secondRun = Executor("default").ExecuteTaskAsync(second, CancellationToken.None);

        // The second node waits for the first one's lock rather than running beside it or skipping.
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.False(second.Started.Task.IsCompleted);

        first.Release.SetResult();
        await Task.WhenAll(firstRun, secondRun).WaitAsync(Bound);

        Assert.True(second.Started.Task.IsCompleted);
        Assert.Equal(1, probe.MostAtOnce);
    }

    [SkippableFact]
    public async Task Another_shell_takes_its_own_lock_and_does_not_wait()
    {
        Skip.If(database.SkipReason is not null, database.SkipReason);
        var probe = new Probe();
        var first = new GatedSingleNodeTask(probe);
        var other = new GatedSingleNodeTask(probe);
        other.Release.SetResult();

        var firstRun = Executor("default").ExecuteTaskAsync(first, CancellationToken.None);
        await first.Started.Task.WaitAsync(Bound);
        await Executor("other").ExecuteTaskAsync(other, CancellationToken.None).WaitAsync(Bound);

        Assert.False(firstRun.IsCompleted);
        Assert.Equal(2, probe.MostAtOnce);
        first.Release.SetResult();
        await firstRun.WaitAsync(Bound);
    }

    [SkippableFact]
    public async Task A_killed_session_cancels_the_running_task_as_a_failure_and_frees_the_lock()
    {
        Skip.If(database.SkipReason is not null, database.SkipReason);
        var task = new GatedSingleNodeTask(new Probe());

        var run = Executor("default").ExecuteTaskAsync(task, CancellationToken.None);
        await task.Started.Task.WaitAsync(Bound);
        await database.KillLockHoldersAsync();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => run.WaitAsync(Bound));
        Assert.True(failure.InnerException is OperationCanceledException, failure.ToString());
        Assert.True(task.Cancelled);
        await using var taken = await Node().TryAcquireLockAsync(GatedSingleNodeTask.LockKey("default"), Bound);
        Assert.NotNull(taken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var node in _nodes)
            await node.DisposeAsync();
    }

    private TaskExecutor Executor(string shellName) =>
        new(Node(), NullLogger<TaskExecutor>.Instance, new ShellSettings(shellName));

    private IDistributedLockProvider Node()
    {
        var services = new ServiceCollection();
        new DatabaseLockingFeature { Provider = database.Provider, ConnectionString = database.ConnectionString }.ConfigureServices(services);
        var node = services.BuildServiceProvider();
        _nodes.Add(node);
        return node.GetRequiredService<IDistributedLockProvider>();
    }

    /// <summary>Counts the gated tasks running at once.</summary>
    private sealed class Probe
    {
        private int _running;
        private int _mostAtOnce;

        public int MostAtOnce => Volatile.Read(ref _mostAtOnce);

        public IDisposable Enter()
        {
            var running = Interlocked.Increment(ref _running);
            int most;
            while (running > (most = Volatile.Read(ref _mostAtOnce)) && Interlocked.CompareExchange(ref _mostAtOnce, running, most) != most)
            {
            }

            return new Exit(this);
        }

        private sealed class Exit(Probe probe) : IDisposable
        {
            public void Dispose() => Interlocked.Decrement(ref probe._running);
        }
    }

    /// <summary>Runs until it is released or its token is cancelled.</summary>
    [SingleNodeTask]
    private sealed class GatedSingleNodeTask(Probe probe) : ITask
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Cancelled { get; private set; }

        public static string LockKey(string shellName) => $"elsa:single-node-task:{shellName}:{typeof(GatedSingleNodeTask).FullName}";

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            using var _ = probe.Enter();
            Started.TrySetResult();
            try
            {
                await Release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelled = true;
                throw;
            }
        }
    }
}

[Collection(PostgreSqlLockDatabase.Collection)]
public sealed class PostgreSqlDatabaseLockTests(PostgreSqlLockDatabase database) : DatabaseLockTests(database);

[Collection(SqlServerLockDatabase.Collection)]
public sealed class SqlServerDatabaseLockTests(SqlServerLockDatabase database) : DatabaseLockTests(database);

[Collection(MySqlLockDatabase.Collection)]
public sealed class MySqlDatabaseLockTests(MySqlLockDatabase database) : DatabaseLockTests(database);
