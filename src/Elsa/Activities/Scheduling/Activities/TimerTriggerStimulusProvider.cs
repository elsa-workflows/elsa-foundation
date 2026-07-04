using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Scheduling.Activities;

/// <summary>
/// The <see cref="IActivityTriggerStimulusProvider"/> for the <see cref="Timer"/> start trigger (W16). It
/// recognizes published <see cref="Timer"/> nodes and derives their stimulus identity from the authored
/// <see cref="Timer.Interval"/> literal, so the trigger extractor indexes the timer at publish time over the
/// pinned artifact and the recurring-trigger pump's dispatched stimulus routes to a start.
/// </summary>
public sealed class TimerTriggerStimulusProvider : IActivityTriggerStimulusProvider
{
    private const string IntervalInput = nameof(Timer.Interval);

    public TriggerStimulusDescriptor? Describe(ExecutableNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (!StringComparer.Ordinal.Equals(node.ActivityType, Timer.ActivityType))
            return null;

        var interval = SchedulingNodeInputs.ReadLiteralString(node, IntervalInput)
            ?? throw new ArgumentException(
                $"Timer trigger node '{node.ExecutableNodeId}' has no literal '{IntervalInput}'. A start trigger's " +
                "interval must be an authored literal so its stimulus is fixed at publish time.");

        return TimerStimulus.DescribeTrigger(interval);
    }
}
