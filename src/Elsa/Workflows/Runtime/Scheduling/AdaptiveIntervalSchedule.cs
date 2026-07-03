using Elsa.Tasks.Core;
using Elsa.Tasks.Schedules;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Scheduling;

/// <summary>
/// <see cref="ITaskSchedule"/> whose interval is re-evaluated before every run through the supplied
/// provider, letting the timer pump widen its interval under sustained failure and snap back once a sweep
/// succeeds. <see cref="ScheduledTaskExecution"/> already re-reads the interval func after each execution,
/// so no extra machinery is needed here.
/// </summary>
public sealed class AdaptiveIntervalSchedule(Func<TimeSpan> intervalProvider, ILogger? logger = null) : ITaskSchedule
{
    public IScheduledTaskExecution ScheduleExecution(Func<Task> action) =>
        new ScheduledTaskExecution(action, intervalProvider, logger);
}
