namespace Elsa.Tasks.Core
{
    public interface ITaskStateManager
    {
        SemaphoreSlim Gate { get; }

        IEnumerable<IRecurringTask> RecurringTasks { get; }

        IEnumerable<IScheduledTaskExecution> ScheduledTaskExecutions { get; }

        IEnumerable<Task> RunningBackgroundTasks { get; }

        CancellationTokenSource? CancellationTokenSource { get; }

        ValueTask Stop();
    }
}
