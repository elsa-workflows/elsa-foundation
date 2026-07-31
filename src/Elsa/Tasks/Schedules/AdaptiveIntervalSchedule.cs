using Elsa.Tasks.Core;
using Microsoft.Extensions.Logging;

namespace Elsa.Tasks.Schedules;

/// <summary>
/// <see cref="ITaskSchedule"/> whose interval is re-evaluated before every run through the supplied provider,
/// letting a pump widen its interval under sustained failure and snap back once a sweep succeeds.
/// </summary>
/// <remarks>
/// <see cref="ScheduledTaskExecution"/> already re-reads the interval func after each execution, so no extra
/// machinery is needed here. This is the adaptive sibling of <see cref="IntervalSchedule"/>, which pins one
/// fixed interval for the lifetime of the schedule.
/// </remarks>
public sealed class AdaptiveIntervalSchedule(Func<TimeSpan> intervalProvider, ILogger? logger = null) : ITaskSchedule
{
    public IScheduledTaskExecution ScheduleExecution(Func<Task> action) =>
        new ScheduledTaskExecution(action, intervalProvider, logger);
}
