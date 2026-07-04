using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Scheduling.Activities;

/// <summary>
/// The <see cref="IRecurringTriggerScheduleProvider"/> for the <see cref="Timer"/> start trigger (W16). It
/// recognizes published <see cref="Timer"/> nodes and derives their recurrence spec from the authored
/// <see cref="Timer.Interval"/> literal, so the schedule indexer writes a recurring schedule keyed by the same
/// stimulus the trigger index binds. The pump fires that schedule to start a new instance on each interval.
/// </summary>
public sealed class TimerRecurringScheduleProvider : IRecurringTriggerScheduleProvider
{
    private const string IntervalInput = nameof(Timer.Interval);

    public RecurringScheduleDescriptor? Describe(ExecutableNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (!StringComparer.Ordinal.Equals(node.ActivityType, Timer.ActivityType))
            return null;

        var interval = SchedulingNodeInputs.ReadLiteralString(node, IntervalInput)
            ?? throw new ArgumentException(
                $"Timer trigger node '{node.ExecutableNodeId}' has no literal '{IntervalInput}'. A recurring " +
                "start trigger's interval must be an authored literal so its schedule is fixed at publish time.");

        return TimerStimulus.DescribeSchedule(interval);
    }
}
