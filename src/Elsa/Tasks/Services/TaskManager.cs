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
public sealed class TaskManager(ILoggerFactory loggerFactory, IServiceProvider serviceProvider) : ITaskManager, IAsyncDisposable
{
    private readonly ILogger<TaskManager> logger = loggerFactory.CreateLogger<TaskManager>();
    private readonly CancellationTokenSource _shutdownCancellationTokenSource = new();
    private int _disposeRequested;
    private TaskStateManager? taskStateManager;

    public async Task StartExecutingRegisteredTasks(CancellationToken token)
    {
        if (Volatile.Read(ref _disposeRequested) == 1)
            return;

        var taskExecutor = serviceProvider.GetRequiredService<ITaskExecutor>();

        var stateManager = GetOrInitializeTaskStateManager(token);
        var cancellationToken = stateManager.GetCancellationToken();

        await stateManager.Gate.WaitAsync(cancellationToken);

        try
        {
            if (Volatile.Read(ref _disposeRequested) == 1)
                return;

            // Stop any previously-started tasks before (re)starting. On the supported path this runs
            // once per shell: CShells builds a fresh provider (and thus a fresh singleton IEventChannel
            // + this singleton manager) per shell generation, so BackgroundTasks is empty here and Stop
            // is a no-op. Re-invoking Start on a live manager is NOT supported: Stop() completes the
            // singleton IEventChannel writer via BackgroundEventPublisher.StopAsync, and a completed
            // channel cannot be reopened — the restarted reader would drain-and-exit and every later
            // background publish would throw. Restart means: dispose this manager (new shell generation).
            await stateManager.Stop();
            stateManager.CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token);
            cancellationToken = stateManager.GetCancellationToken();

            // Step 1: Run startup tasks (with dependency ordering)
            await RunStartupTasks(serviceProvider, taskExecutor, cancellationToken);

            // Step 2: Run background tasks
            await RunBackgroundTasks(serviceProvider, taskExecutor, stateManager, cancellationToken);

            // Step 3: Start recurring tasks
            await StartRecurringTasksAsync(serviceProvider, taskExecutor, stateManager, cancellationToken);
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

    private TaskStateManager GetOrInitializeTaskStateManager(CancellationToken token)
    {
        if (taskStateManager is not null)
            return taskStateManager;

        var activationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdownCancellationTokenSource.Token);
        taskStateManager = new TaskStateManager(loggerFactory.CreateLogger<TaskStateManager>(), activationTokenSource);

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
        var topologicalSorter = scopedProvider.GetService<ITopologicalTaskSorter>();

        var tasksToExecute = topologicalSorter is not null
            ? topologicalSorter.Sort(startupTasks)
            : startupTasks;

        foreach (var task in tasksToExecute)
        {
            await taskExecutor.ExecuteTaskAsync(task, cancellationToken);
        }
    }

    private static Task RunBackgroundTasks(IServiceProvider serviceProvider, ITaskExecutor taskExecutor, TaskStateManager stateManager, CancellationToken cancellationToken)
    {
        var backgroundTasks = serviceProvider.GetServices<IBackgroundTask>();
        var backgroundTaskStarter = serviceProvider.GetRequiredService<IBackgroundTaskStarter>();
        var tenantCancellationToken = stateManager.CancellationTokenSource?.Token ?? cancellationToken;

        foreach (var backgroundTask in backgroundTasks)
        {
            // Track the instance so TaskStateManager can signal it to stop gracefully on shutdown (IN-5).
            stateManager.BackgroundTasks.Add(backgroundTask);

            var task = backgroundTaskStarter
                .StartAsync(backgroundTask, tenantCancellationToken)
                .ContinueWith(
                    _ => taskExecutor.ExecuteTaskAsync(backgroundTask, tenantCancellationToken),
                    cancellationToken,
                    TaskContinuationOptions.RunContinuationsAsynchronously,
                    TaskScheduler.Default
                )
                .Unwrap();

            if (!task.IsCompleted)
                stateManager.RunningBackgroundTasks.Add(task);
        }

        return Task.CompletedTask;
    }

    private async Task StartRecurringTasksAsync(IServiceProvider serviceProvider, ITaskExecutor taskExecutor, TaskStateManager state, CancellationToken cancellationToken)
    {
        var recurringTasks = serviceProvider.GetServices<IRecurringTask>().ToList();
        var tenantCancellationToken = state.CancellationTokenSource?.Token ?? cancellationToken;

        foreach (var task in recurringTasks)
        {
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
            state.RecurringTasks.Add(task);
            await task.StartAsync(cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeRequested, 1) == 1)
            return;

        try
        {
            await _shutdownCancellationTokenSource.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed by a concurrent path.
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
}