using Elsa.Tasks.Core;
using Elsa.Tasks.Extension;
using Microsoft.Extensions.Logging;

namespace Elsa.Tasks.Services
{
    public sealed class TaskStateManager(ILogger<TaskStateManager> logger, CancellationTokenSource cancellationTokenSource)
                : ITaskStateManager
    {
        // SemaphoreSlim only allocates a kernel handle when AvailableWaitHandle is accessed.
        // Since we exclusively use WaitAsync(), no kernel handle is ever created and disposal is a no-op.
        private readonly SemaphoreSlim _gate = new(1, 1);

        public SemaphoreSlim Gate => _gate;
        internal List<Task> RunningBackgroundTasks { get; } = [];
        internal List<IScheduledTaskExecution> ScheduledTaskExecutions { get; } = [];
        internal List<IRecurringTask> RecurringTasks { get; } = [];
        public CancellationTokenSource? CancellationTokenSource { get; internal set; } = cancellationTokenSource;

        internal CancellationToken GetCancellationToken() => CancellationTokenSource?.Token ?? CancellationToken.None;

        IEnumerable<IRecurringTask> ITaskStateManager.RecurringTasks => RecurringTasks;

        IEnumerable<IScheduledTaskExecution> ITaskStateManager.ScheduledTaskExecutions => ScheduledTaskExecutions;

        IEnumerable<Task> ITaskStateManager.RunningBackgroundTasks => RunningBackgroundTasks;

        public async ValueTask Stop()
        {
            await CancelCancellationTokenSource();
            await DisposeRecurringTasks();
            await DisposeScheduledExecutions();
            await DisposeBackgroundRunningTasks();
            await DisposeCancellationTokenSource();
        }

        async ValueTask DisposeCancellationTokenSource()
        {
            if (CancellationTokenSource == null)
                return;

            try
            {
                CancellationTokenSource.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed by a concurrent path.
            }
            catch (Exception e) when (!e.IsFatal())
            {
                logger.LogWarning(e, "Failed to dispose task cancellation token source");
            }

            CancellationTokenSource = null;
        }

        async ValueTask CancelCancellationTokenSource()
        {
            if (CancellationTokenSource is null)
                return;

            try
            {
                await CancellationTokenSource.CancelAsync();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed by a concurrent path.
            }
            catch (Exception e) when (!e.IsFatal())
            {
                logger.LogWarning(e, "Failed to cancel task cancellation token source while stopping all tasks");
            }
        }

        async ValueTask DisposeBackgroundRunningTasks()
        {
            try
            {
                await Task.WhenAll(RunningBackgroundTasks);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested.
            }
            catch (AggregateException e)
            {
                logger.LogError(e, "One or more background tasks failed while stopping tenant tasks");
            }
            catch (InvalidOperationException e)
            {
                logger.LogError(e, "Background task collection was in an invalid state while stopping tenant tasks");
            }

            RunningBackgroundTasks.Clear();
        }

        async ValueTask DisposeScheduledExecutions()
        {
            foreach (var task in ScheduledTaskExecutions)
            {
                try
                {
                    await task.DisposeAsync();
                }
                catch (ObjectDisposedException)
                {
                    // Timer is already disposed; this can happen during concurrent shutdown paths.
                }
                catch (Exception e) when (!e.IsFatal())
                {
                    logger.LogWarning(e, "Failed to dispose a scheduled task execution while stopping all tasks");
                }
            }

            ScheduledTaskExecutions.Clear();
        }

        async ValueTask DisposeRecurringTasks()
        {
            foreach (var task in RecurringTasks)
            {
                try
                {
                    await task.StopAsync(CancellationToken.None);
                }
                catch (ObjectDisposedException)
                {
                    // Timer is already disposed; this can happen during concurrent shutdown paths.
                }
                catch (Exception e) when (!e.IsFatal())
                {
                    logger.LogError(e, "Failed to stop recurring task {TaskType}", task.GetType().Name);
                }
            }

            RecurringTasks.Clear();
        }
    }
}
