using CShells;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Services;
using Elsa.Locking.Core;
using Elsa.Primitives.Diagnostics;
using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using Elsa.Tasks.Diagnostics;
using Elsa.Tasks.Extension;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

namespace Elsa.Tasks.Services;

/// <summary>
/// Runs tasks, applying what a task class declares: <c>[RequiresSchemaVersion]</c> first, then <c>[SingleNodeTask]</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Dormancy before the lock.</b> A task whose schema requirements are unmet on this node is skipped before anything
/// else, so a dormant node never holds the single-node lock while a node that could run the task waits for it.
/// </para>
/// <para>
/// <b>[SingleNodeTask]: one at a time, then run.</b> The task waits for a lock keyed by its shell and type, for at most
/// the lock provider's acquisition timeout, and runs once it holds it; it is never skipped because another node is running
/// it. When the wait runs out it fails, naming the lock. Losing the lock while the task runs cancels the task's token, and
/// the cancellation surfaces as a failure, never as an orderly cancellation. There is no failover, no fencing and no record
/// that the task ran.
/// </para>
/// </remarks>
public sealed class TaskExecutor(
    IDistributedLockProvider distributedLockProvider,
    ILogger<TaskExecutor> logger,
    ShellSettings shellSettings,
    ISchemaDormancyCheck? dormancyCheck = null) : ITaskExecutor, IBackgroundTaskStarter
{
    /// <summary>The start of every <c>[SingleNodeTask]</c> lock key; the shell's name and the task's type name follow it.</summary>
    private const string SingleNodeLockKeyPrefix = "elsa:single-node-task:";

    /// <summary>Background tasks whose start was skipped because this node was dormant for them; their stop is skipped too.</summary>
    private readonly ConcurrentDictionary<IBackgroundTask, byte> _skippedStarts = new(ReferenceEqualityComparer.Instance);

    public async Task ExecuteTaskAsync(ITask task, CancellationToken cancellationToken)
    {
        if (task is IStartupTask)
        {
            await ExecuteStartupTaskAsync(task, cancellationToken);
            return;
        }

        await ExecuteInternalAsync(task, task.ExecuteAsync, cancellationToken);
    }

    public async Task StartAsync(IBackgroundTask task, CancellationToken cancellationToken)
    {
        if (await ExecuteInternalAsync(task, task.StartAsync, cancellationToken))
            _skippedStarts.TryRemove(task, out _); // a start that runs supersedes an earlier skip, so the task is stopped normally
        else
            _skippedStarts[task] = 0;
    }

    public async Task StopAsync(IBackgroundTask task, CancellationToken cancellationToken)
    {
        // For callers of IBackgroundTaskStarter: a task that started is stopped even if this node has since gone dormant,
        // and a task whose start was skipped has nothing to stop. (TaskStateManager stops tasks directly, not through here.)
        if (_skippedStarts.TryRemove(task, out _))
            return;

        await ExecuteInternalAsync(task, task.StopAsync, checkDormancy: false, cancellationToken);
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
            var executed = await ExecuteInternalAsync(task, task.ExecuteAsync, cancellationToken);
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

    /// <returns><c>false</c> when the task was skipped because this node is dormant for it; <c>true</c> when it ran.</returns>
    private Task<bool> ExecuteInternalAsync(ITask task, Func<CancellationToken, Task> action, CancellationToken cancellationToken) =>
        ExecuteInternalAsync(task, action, checkDormancy: true, cancellationToken);

    private async Task<bool> ExecuteInternalAsync(ITask task, Func<CancellationToken, Task> action, bool checkDormancy, CancellationToken cancellationToken)
    {
        var taskType = task.GetType();
        if (checkDormancy && !await IsAvailableAsync(taskType, cancellationToken))
            return false;

        if (taskType.GetCustomAttribute<SingleNodeTaskAttribute>() is null)
        {
            await action(cancellationToken);
            return true;
        }

        var lockKey = SingleNodeLockKey(taskType);
        var handle = await AcquireSingleNodeLockAsync(taskType, lockKey, cancellationToken);
        try
        {
            await RunHoldingLockAsync(handle, taskType, lockKey, action, cancellationToken);
        }
        finally
        {
            await ReleaseAsync(handle, taskType, lockKey);
        }

        return true;
    }

    /// <summary>
    /// Releases the lock without ever replacing the task's own outcome. Releasing a lock whose connection was lost fails, and
    /// left to throw out of the <c>finally</c> it would replace the lost-lock failure with a provider error that says
    /// nothing about the task. A database releases a session lock when its connection closes anyway.
    /// </summary>
    private async ValueTask ReleaseAsync(IDistributedSynchronizationHandle handle, Type taskType, string lockKey)
    {
        try
        {
            await handle.DisposeAsync();
        }
        catch (Exception exception) when (!exception.IsFatal())
        {
            logger.LogWarning(exception, "Could not release lock '{LockKey}' after single-node task '{TaskType}'.", lockKey, taskType.FullName);
        }
    }

    /// <summary>
    /// Whether the schema requirements <paramref name="taskType"/> declares are met here, asked before any lock is taken. A
    /// shell that composes no dormancy check has observed nothing, so a declared requirement is unmet there, never assumed
    /// met (spec 182, FR-003): the task is skipped, with a warning, because it can never run in that composition.
    /// </summary>
    private async ValueTask<bool> IsAvailableAsync(Type taskType, CancellationToken cancellationToken)
    {
        var requirements = SchemaVersionRequirement.DeclaredBy(taskType);
        if (requirements.Count == 0)
            return true;

        var availability = dormancyCheck is null
            ? SchemaDormancyRule.Evaluate(requirements, _ => null)
            : await dormancyCheck.EvaluateAsync(requirements, cancellationToken);
        if (availability.IsAvailable)
            return true;

        if (dormancyCheck is null)
            logger.LogWarning(
                "Skipping task '{TaskType}': it requires {Requirements}, and this shell composes no schema dormancy check to observe them. {Reason}",
                taskType.FullName,
                string.Join(", ", requirements),
                availability.Reason);
        else if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Skipping task '{TaskType}' while this node is dormant for it: {Reason}", taskType.FullName, availability.Reason);

        return false;
    }

    /// <summary>
    /// The lock key: per shell, so two shells of one process never contend, and per type name without the assembly
    /// version, so two releases of the task on different nodes during a rolling upgrade still take the same lock.
    /// </summary>
    private string SingleNodeLockKey(Type taskType)
    {
        var typeName = taskType.FullName ?? taskType.Name;
        return $"{SingleNodeLockKeyPrefix}{shellSettings.Id.Name}:{typeName}";
    }

    private async Task<IDistributedSynchronizationHandle> AcquireSingleNodeLockAsync(Type taskType, string lockKey, CancellationToken cancellationToken)
    {
        var handle = await distributedLockProvider.TryAcquireLockAsync(lockKey, TimeSpan.Zero, cancellationToken);
        if (handle is not null)
            return handle;

        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation(
                "Single-node task '{TaskType}' is waiting for lock '{LockKey}', which another node or process of this shell holds; it runs once the lock is released.",
                taskType.FullName,
                lockKey);

        // No timeout here means the provider's own acquisition timeout, which bounds the wait.
        return await distributedLockProvider.TryAcquireLockAsync(lockKey, cancellationToken: cancellationToken)
               ?? throw new TimeoutException(
                   $"Single-node task '{taskType.FullName}' did not run: lock '{lockKey}' was still held by another node or process " +
                   "when the lock provider's acquisition timeout ran out. The holder is still running the task, or it hung. Raise the " +
                   "locking feature's LockAcquisitionTimeoutMinutes if the task legitimately takes longer than that.");
    }

    private async Task RunHoldingLockAsync(
        IDistributedSynchronizationHandle handle,
        Type taskType,
        string lockKey,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        // Read once: a provider that detects loss may start monitoring the connection when this is first read.
        var lost = handle.HandleLostToken;
        if (!lost.CanBeCanceled)
        {
            await action(cancellationToken);
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lost);
        try
        {
            await action(linked.Token);
        }
        catch (OperationCanceledException exception) when (lost.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // A lost lock is a fault, not a shutdown. Left as a cancellation, it would be reported and logged as one, and a
            // recurring task's cancellation is logged at Information: the fault would disappear from the log.
            throw new InvalidOperationException(
                $"Single-node task '{taskType.FullName}' was cancelled because it lost lock '{lockKey}' while it ran, so another " +
                "node or process may have taken the lock and started the task too.",
                exception);
        }

        if (lost.IsCancellationRequested)
            logger.LogWarning(
                "Single-node task '{TaskType}' completed after it lost lock '{LockKey}'; another node or process may have run it at the same time.",
                taskType.FullName,
                lockKey);
    }
}
