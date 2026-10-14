using Cronos;
using Elsa.Primitives.Contracts;
using Elsa.Tasks.Core;
using Microsoft.Extensions.Logging;

namespace Elsa.Tasks.Schedules;

/// <summary>
/// The schedules an <see cref="IRecurringTask"/> can return from <see cref="IRecurringTask.GetSchedule"/>: a fixed
/// interval or a cron expression.
/// </summary>
/// <remarks>
/// The task manager logs a run that fails. The logger given here records only what escapes that, so a run can never
/// fail without a trace.
/// </remarks>
public static class TaskSchedule
{
    /// <summary>
    /// Runs the task one <paramref name="interval"/> after the schedule starts, and then one <paramref name="interval"/>
    /// after each run ends.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="interval"/> is zero or negative.</exception>
    public static ITaskSchedule Every(TimeSpan interval, ILogger logger)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(logger);
        return new IntervalSchedule(interval, logger);
    }

    /// <summary>
    /// Runs the task at the next occurrence of <paramref name="expression"/>, a standard five-field cron expression read
    /// in UTC, after the schedule starts, and then at the next occurrence after each run ends.
    /// </summary>
    /// <exception cref="FormatException"><paramref name="expression"/> is not a five-field cron expression.</exception>
    /// <exception cref="ArgumentException"><paramref name="expression"/> never occurs, such as one for February 30.</exception>
    public static ITaskSchedule Cron(string expression, ISystemClock systemClock, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        ArgumentNullException.ThrowIfNull(systemClock);
        ArgumentNullException.ThrowIfNull(logger);

        var cron = CronExpression.Parse(expression);
        if (cron.GetNextOccurrence(systemClock.UtcNow, TimeZoneInfo.Utc) is null)
            throw new ArgumentException($"The cron expression '{expression}' never occurs.", nameof(expression));

        return new CronSchedule(systemClock, cron, logger);
    }
}
