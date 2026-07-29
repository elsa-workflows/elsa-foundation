using Elsa.Locking.Core;
using Elsa.Primitives.Diagnostics;
using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using Elsa.Tasks.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Reflection;

namespace Elsa.Tasks.Services;

public sealed class TaskExecutor(IDistributedLockProvider distributedLockProvider, ILogger<TaskExecutor> logger) : ITaskExecutor, IBackgroundTaskStarter
{
    public async Task ExecuteTaskAsync(ITask task, CancellationToken cancellationToken)
    {
        if (task is IStartupTask)
        {
            await ExecuteStartupTaskAsync(task, cancellationToken);
            return;
        }

        await ExecuteInternalAsync(task, () => task.ExecuteAsync(cancellationToken), cancellationToken);
    }

    public async Task StartAsync(IBackgroundTask task, CancellationToken cancellationToken)
    {
        await ExecuteInternalAsync(task, () => task.StartAsync(cancellationToken), cancellationToken);
    }

    public async Task StopAsync(IBackgroundTask task, CancellationToken cancellationToken)
    {
        await ExecuteInternalAsync(task, () => task.StopAsync(cancellationToken), cancellationToken);
    }

    private async Task ExecuteStartupTaskAsync(ITask task, CancellationToken cancellationToken)
    {
        var taskType = task.GetType().FullName ?? task.GetType().Name;
        var started = Stopwatch.GetTimestamp();
        var outcome = StartupTaskTelemetry.SuccessOutcome;
        using var activity = ObservationalTelemetryScope.Start(
            StartupTaskTelemetry.GetActivitySource,
            StartupTaskTelemetry.ActivityName);

        try
        {
            var executed = await ExecuteInternalAsync(task, () => task.ExecuteAsync(cancellationToken), cancellationToken);
            if (!executed)
                outcome = StartupTaskTelemetry.SkippedOutcome;
        }
        catch (OperationCanceledException)
        {
            outcome = StartupTaskTelemetry.CancelledOutcome;
            activity.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        catch (Exception)
        {
            outcome = StartupTaskTelemetry.FailedOutcome;
            activity.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        finally
        {
            var durationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var tags = new TagList
            {
                { StartupTaskTelemetry.TaskTypeTag, taskType },
                { StartupTaskTelemetry.OutcomeTag, outcome }
            };
            activity.SetTag(StartupTaskTelemetry.TaskTypeTag, taskType);
            activity.SetTag(StartupTaskTelemetry.OutcomeTag, outcome);
            activity.Observe(
                StartupTaskTelemetry.GetDuration,
                histogram => histogram.Record(durationMs, tags));
            logger.LogInformation(
                "Startup task {TaskType} completed with outcome {Outcome} after {DurationMs:F3} ms",
                taskType,
                outcome,
                durationMs);
        }
    }

    private async Task<bool> ExecuteInternalAsync(ITask task, Func<Task> action, CancellationToken cancellationToken)
    {
        var taskType = task.GetType();
        var singleNodeTask = taskType.GetCustomAttribute<SingleNodeTaskAttribute>() != null;

        if (!singleNodeTask)
        {
            await action();
            return true;
        }

        // [SingleNodeTask] means "run on exactly one node; other nodes skip". Use a non-blocking try-acquire so a
        // secondary node whose peer already holds the lock skips immediately instead of blocking for the full lock
        // acquisition timeout (up to 10 minutes by default) and then crashing startup with a TimeoutException.
        // Only a null (unavailable) handle skips; a throwing lock provider still surfaces its failure.
        var resourceName = taskType.AssemblyQualifiedName!;
        await using var handle = await distributedLockProvider.TryAcquireLockAsync(resourceName, timeout: TimeSpan.Zero, cancellationToken: cancellationToken);

        if (handle is null)
        {
            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation("Skipping single-node task '{TaskType}' on this node: the single-node lock is already held by another node.", taskType.FullName);

            return false;
        }

        await action();
        return true;
    }
}
