using Elsa.Tasks.Core;
using Microsoft.Extensions.Logging;

namespace Elsa.Tasks.Schedules
{
    public sealed class IntervalSchedule(TimeSpan interval, ILogger logger) : ITaskSchedule
    {  
        public IScheduledTaskExecution ScheduleExecution(Func<Task> action)
        {
            return new ScheduledTaskExecution(action, () => interval, logger);
        }
    }
}
