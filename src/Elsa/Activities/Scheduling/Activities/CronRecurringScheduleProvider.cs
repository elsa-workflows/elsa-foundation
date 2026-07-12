using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Scheduling.Activities;

/// <summary>
/// The <see cref="IRecurringTriggerScheduleProvider"/> for the <see cref="Cron"/> start trigger (W16). It
/// recognizes published <see cref="Cron"/> nodes and derives their recurrence spec from the authored
/// <see cref="Cron.Expression"/> literal, so the schedule indexer writes a recurring schedule keyed by the same
/// stimulus the trigger index binds. The pump fires that schedule to start a new instance on each occurrence.
/// </summary>
public sealed class CronRecurringScheduleProvider : IRecurringTriggerScheduleProvider
{
    public string ProviderId => "Elsa.Cron";

    private const string ExpressionInput = nameof(Cron.Expression);

    public RecurringScheduleDescriptor? Describe(ExecutableNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (!StringComparer.Ordinal.Equals(node.ActivityType, Cron.ActivityType))
            return null;

        var expression = SchedulingNodeInputs.ReadLiteralString(node, ExpressionInput)
            ?? throw new ArgumentException(
                $"Cron trigger node '{node.ExecutableNodeId}' has no literal '{ExpressionInput}'. A recurring " +
                "start trigger's cron expression must be an authored literal so its schedule is fixed at publish time.");

        return CronStimulus.DescribeSchedule(expression);
    }
}
