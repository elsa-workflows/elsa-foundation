using Cronos;
using Elsa.Primitives.Contracts;
using Elsa.Tasks.Core;
using Microsoft.Extensions.Logging;

namespace Elsa.Tasks.Schedules
{
    public sealed class CronSchedule(ISystemClock systemClock, CronExpression expression, ILogger logger) : ITaskSchedule
    {
        public IScheduledTaskExecution ScheduleExecution(Func<Task> action)
        {
            return new ScheduledTaskExecution(
                action, 
                () => expression.GetNextOccurrence(systemClock.UtcNow.DateTime)!.Value - systemClock.UtcNow.DateTime,
                logger
            );
        }
    }
}
