using Cronos;
using Elsa.Primitives.Contracts;
using Elsa.Tasks.Core;
using Microsoft.Extensions.Logging;

namespace Elsa.Tasks.Schedules;

internal sealed class CronSchedule(ISystemClock systemClock, CronExpression expression, ILogger logger) : ITaskSchedule
{
    public IScheduledTaskExecution ScheduleExecution(Func<Task> action) => new ScheduledTaskExecution(action, NextWait, logger);

    // One clock read, so the wait runs to the next occurrence after that same instant. The DateTimeOffset overload,
    // because the DateTime one refuses any DateTime whose kind is not UTC, and DateTimeOffset.DateTime never is.
    private TimeSpan NextWait()
    {
        var now = systemClock.UtcNow;
        if (expression.GetNextOccurrence(now, TimeZoneInfo.Utc) is { } next)
            return next - now;

        // Throwing here would escape the timer callback; stop scheduling instead, and say so.
        logger.LogWarning("The cron expression {CronExpression} has no further occurrence; the task will not run again", expression.ToString());
        return Timeout.InfiniteTimeSpan;
    }
}
