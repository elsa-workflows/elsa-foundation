using Elsa.Tasks.Core;
using Elsa.Tasks.Schedules;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Distributed.Services;

/// <summary>
/// <see cref="ITaskSchedule"/> whose interval is re-evaluated before every run through the supplied provider, letting
/// the placement pump widen its interval under sustained failure and snap back once a sweep succeeds.
/// </summary>
public sealed class AdaptiveIntervalSchedule(Func<TimeSpan> intervalProvider, ILogger? logger = null) : ITaskSchedule
{
    public IScheduledTaskExecution ScheduleExecution(Func<Task> action) =>
        new ScheduledTaskExecution(action, intervalProvider, logger);
}
