using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Scheduling.Activities;

/// <summary>
/// The <see cref="IActivityTriggerStimulusProvider"/> for the <see cref="Cron"/> start trigger (W16). It
/// recognizes published <see cref="Cron"/> nodes and derives their stimulus identity from the authored
/// <see cref="Cron.Expression"/> literal, so the trigger extractor indexes the cron trigger at publish time over
/// the pinned artifact and the recurring-trigger pump's dispatched stimulus routes to a start.
/// </summary>
public sealed class CronTriggerStimulusProvider : IActivityTriggerStimulusProvider
{
    private const string ExpressionInput = nameof(Cron.Expression);

    public IReadOnlyCollection<TriggerStimulusDescriptor> Describe(ExecutableNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (!StringComparer.Ordinal.Equals(node.ActivityType, Cron.ActivityType))
            return [];

        var expression = SchedulingNodeInputs.ReadLiteralString(node, ExpressionInput)
            ?? throw new ArgumentException(
                $"Cron trigger node '{node.ExecutableNodeId}' has no literal '{ExpressionInput}'. A start trigger's " +
                "cron expression must be an authored literal so its stimulus is fixed at publish time.");

        return [CronStimulus.DescribeTrigger(expression)];
    }
}
