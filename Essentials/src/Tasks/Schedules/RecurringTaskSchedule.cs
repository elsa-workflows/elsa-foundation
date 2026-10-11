using Elsa.Tasks.Core;
using Elsa.Tasks.Schedules.Expressions;

namespace Elsa.Tasks.Schedules;

public sealed class RecurringTaskSchedule(IDictionary<Type, IntervalExpression> scheduledTasks)
{
    public IDictionary<Type, IntervalExpression> ScheduledTasks { get; } = scheduledTasks;

    public void ConfigureTask<T>(TimeSpan interval) where T : IRecurringTask
    {
        ConfigureTask<T>(IntervalExpression.FromInterval(interval));
    }

    public void ConfigureTask<T>(string cronExpression) where T : IRecurringTask
    {
        ConfigureTask<T>(IntervalExpression.FromCron(cronExpression));
    }

    public void ConfigureTask<T>(IntervalExpression intervalExpression) where T : IRecurringTask
    {
        ConfigureTask(typeof(T), intervalExpression);
    }

    public void ConfigureTask(Type recurringTaskType, IntervalExpression intervalExpression)
    {
        ScheduledTasks[recurringTaskType] = intervalExpression;
    }
}