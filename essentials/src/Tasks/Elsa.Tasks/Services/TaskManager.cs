using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using Elsa.Tasks.Extension;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace Elsa.Tasks.Services;

/// <summary>
/// Manages the lifecycle of startup, background, and recurring tasks for tenants.
/// Executes tasks in the proper sequence: startup tasks first, then background tasks, then recurring tasks.
/// </summary>
internal sealed class TaskManager(ILoggerFactory loggerFactory, IServiceProvider serviceProvider) : ITaskManager, IAsyncDisposable
{
    private readonly ILogger<TaskManager> logger = loggerFactory.CreateLogger<TaskManager>();
    private readonly CancellationTokenSource _shutdownCancellationTokenSource = new();
    private readonly object _stopLock = new();
    private int _disposeRequested;
    private TaskStateManager? taskStateManager;
    private Task? _stopTask;

    public async Task StartExecutingRegisteredTasks(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource startupCancellation;
        TaskStateManager stateManager;
        lock (_stopLock)
        {
            if (Volatile.Read(ref _disposeRequested) == 1)
                return;

            // Publish the state and link startup before Stop can dispose the shutdown source.
            stateManager = GetOrInitializeTaskStateManager();
            startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCancellationTokenSource.Token);
        }

        using var startupLifetime = startupCancellation;
        var startupToken = startupCancellation.Token;
        await stateManager.Gate.WaitAsync(startupToken);

        try
        {
            if (Volatile.Read(ref _disposeRequested) == 1)
                return;

            var taskExecutor = serviceProvider.GetRequiredService<ITaskExecutor>();

            // Stop any previously-started tasks before (re)starting. On the supported path this runs
            // once per shell: CShells builds a fresh provider (and thus a fresh singleton IEventChannel
            // + this singleton manager) per shell generation, so BackgroundTasks is empty here and Stop
            // is a no-op. Re-invoking Start on a live manager is NOT supported: Stop() completes the
            // singleton IEventChannel writer via BackgroundEventPublisher.StopAsync, and a completed
            // channel cannot be reopened — the restarted reader would drain-and-exit and every later
            // background publish would throw. Restart means: dispose this manager (new shell generation).
            await stateManager.Stop();
            // An activation caller may cancel its token after startup has succeeded. Running tasks belong
            // to the shell, and graceful Stop must drain them before cancelling this independent lifetime.
            stateManager.CancellationTokenSource = new CancellationTokenSource();

            // Step 1: Run startup tasks (with dependency ordering)
            await RunStartupTasks(serviceProvider, taskExecutor, startupToken);

            // Step 2: Run background tasks
            await RunBackgroundTasks(serviceProvider, taskExecutor, stateManager, startupToken);

            // Step 3: Start recurring tasks
            await StartRecurringTasksAsync(serviceProvider, taskExecutor, stateManager, startupToken);
            startupToken.ThrowIfCancellationRequested();
        }
        catch (Exception e)
        {
            logger.LogError(e, "An error occurred while starting tasks.");
            await stateManager.Stop();
            throw;
        }
        finally
        {
            stateManager.Gate.Release();
        }
    }

    private TaskStateManager GetOrInitializeTaskStateManager()
    {
        if (taskStateManager is not null)
            return taskStateManager;

        // Double-checked locking: two concurrent first callers must not each construct a TaskStateManager
        // (and its CancellationTokenSource) and race to publish theirs, orphaning the loser's.
        lock (_stopLock)
        {
            if (taskStateManager is not null)
                return taskStateManager;

            taskStateManager = new TaskStateManager(loggerFactory.CreateLogger<TaskStateManager>(), new CancellationTokenSource());
        }

        return taskStateManager;
    }

    private static async Task RunStartupTasks(IServiceProvider serviceProvider, ITaskExecutor taskExecutor, CancellationToken cancellationToken)
    {
        // Startup tasks are scoped (migrations, seeding, activity registration — they use scoped
        // stores/DbContexts). TaskManager is a shell-singleton, so its injected provider is the shell
        // root; resolve and run the startup tasks in a dedicated scope. Background and recurring tasks
        // are shell-singletons and are resolved from the root provider so they outlive this scope.
        await using var scope = serviceProvider.CreateAsyncScope();
        var scopedProvider = scope.ServiceProvider;

        var startupTasks = scopedProvider.GetServices<IStartupTask>()
            .OrderBy(x => x.GetType().GetCustomAttribute<OrderAttribute>()?.Order ?? 0f)
            .ToList();

        // First apply OrderAttribute to determine a base order, then perform topological sorting.
        // The topological sort is the final ordering step to ensure dependency constraints are respected.
        // The sorter is required: running the tasks unsorted would drop [TaskDependency] ordering without an error.
        var tasksToExecute = scopedProvider.GetRequiredService<TopologicalTaskSorter>().Sort(startupTasks);

        foreach (var task in tasksToExecute)
        {
            await taskExecutor.ExecuteTaskAsync(task, cancellationToken);
        }
    }

    private static async Task RunBackgroundTasks(IServiceProvider serviceProvider, ITaskExecutor taskExecutor, TaskStateManager stateManager, CancellationToken cancellationToken)
    {
        var backgroundTasks = serviceProvider.GetServices<IBackgroundTask>();
        var backgroundTaskStarter = serviceProvider.GetRequiredService<TaskExecutor>();
        var tenantCancellationToken = stateManager.GetCancellationToken();

        foreach (var backgroundTask in backgroundTasks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Track before Start so partial startup is also stopped if its hook fails or is cancelled.
            stateManager.BackgroundTasks.Add(backgroundTask);
            await backgroundTaskStarter.StartAsync(backgroundTask, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            stateManager.RunningBackgroundTasks.Add(Task.Run(
                () => taskExecutor.ExecuteTaskAsync(backgroundTask, tenantCancellationToken), CancellationToken.None));
        }
    }

    private async Task StartRecurringTasksAsync(IServiceProvider serviceProvider, ITaskExecutor taskExecutor, TaskStateManager state, CancellationToken cancellationToken)
    {
        var recurringTasks = serviceProvider.GetServices<IRecurringTask>().ToList();
        var tenantCancellationToken = state.GetCancellationToken();

        foreach (var task in recurringTasks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            state.RecurringTasks.Add(task);
            await task.StartAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var schedule = task.GetSchedule();
            var scheduledExecution = schedule.ScheduleExecution(async () =>
            {
                if (tenantCancellationToken.IsCancellationRequested)
                    return;

                try
                {
                    await taskExecutor.ExecuteTaskAsync(task, tenantCancellationToken);
                }
                catch (OperationCanceledException e)
                {
                    if (logger.IsEnabled(LogLevel.Information))
                        logger.LogInformation(e, "Recurring task {TaskType} was cancelled", task.GetType().Name);
                }
                catch (Exception e) when (!e.IsFatal())
                {
                    // Log but don't rethrow - recurring tasks should not crash the host
                    logger.LogError(e, "Recurring task {TaskType} failed with an error", task.GetType().Name);
                }
            });

            state.ScheduledTaskExecutions.Add(scheduledExecution);
        }
    }

    /// <inheritdoc />
    public Task StopExecutingRegisteredTasks(CancellationToken cancellationToken = default)
    {
        Task stopTask;
        lock (_stopLock)
        {
            // The terminator and provider disposal must await the same operation. If CShells abandons a
            // cancelled terminator, disposal still observes the in-flight stop instead of returning early
            // while that operation continues to use shell services.
            stopTask = _stopTask ??= StopCoreAsync();
        }

        return stopTask.WaitAsync(cancellationToken);
    }

    private async Task StopCoreAsync()
    {
        Interlocked.Exchange(ref _disposeRequested, 1);

        try
        {
            await _shutdownCancellationTokenSource.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed by a concurrent path.
        }
        catch (AggregateException e) when (e.Flatten().InnerExceptions.All(exception => !exception.IsFatal()))
        {
            // Cancellation still reaches the other callbacks. Join startup and finish teardown even if one fails.
            logger.LogWarning(e, "A startup cancellation callback failed while stopping tenant tasks");
        }

        if (taskStateManager is not null)
        {
            await taskStateManager.Gate.WaitAsync();

            try
            {
                await taskStateManager.Stop();
            }
            finally
            {
                taskStateManager.Gate.Release();
            }
        }

        try
        {
            _shutdownCancellationTokenSource.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed by a concurrent path.
        }
    }

    public ValueTask DisposeAsync() => new(StopExecutingRegisteredTasks());
}
