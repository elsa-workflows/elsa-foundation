namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// The recurring-start schedule a Timer/Cron start-trigger activity node declares, described at publish time
/// (W16). A recurring-schedule provider reads the published node's literal inputs and returns this descriptor;
/// the schedule indexer turns it into a durable <see cref="RecurringTriggerSchedule"/> (computing the initial
/// <see cref="RecurringTriggerSchedule.NextOccurrence"/> from the recurrence spec).
/// </summary>
/// <remarks>
/// The (<see cref="StimulusType"/>, <see cref="StimulusHash"/>) pair is the same one the node's
/// <c>IActivityTriggerStimulusProvider</c> returns to the trigger index, so the pump's dispatched stimulus and
/// the router's trigger binding for the node line up. The provider derives everything from the pinned published
/// node — never a running workflow — so the schedule is fixed at publish time.
/// </remarks>
public sealed class RecurringScheduleDescriptor
{
    public RecurringScheduleDescriptor(string stimulusType, string stimulusHash, RecurringScheduleKind kind, string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        StimulusType = stimulusType;
        StimulusHash = stimulusHash;
        Kind = kind;
        Expression = expression;
    }

    public string StimulusType { get; }
    public string StimulusHash { get; }
    public RecurringScheduleKind Kind { get; }

    /// <summary>The recurrence spec: an ISO-8601 duration (Interval) or a cron string (Cron).</summary>
    public string Expression { get; }
}
