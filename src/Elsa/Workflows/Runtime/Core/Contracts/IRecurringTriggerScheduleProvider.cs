using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Describes the recurring-start schedule a Timer/Cron start-trigger activity node declares, at publish time
/// (W16). One provider is registered per recurring-trigger activity type; the schedule indexer asks each
/// provider to describe a node it recognizes. Returning <c>null</c> means "not my activity type" — the indexer
/// moves on to the next provider.
/// </summary>
/// <remarks>
/// This is the recurring-schedule sibling of <see cref="IActivityTriggerStimulusProvider"/>: that seam yields
/// the stimulus identity for the trigger index, this one yields the recurrence spec for the schedule store. A
/// recurring-trigger activity contributes both — the same node produces one trigger binding (so the router can
/// route the pump's stimulus) and one schedule (so the pump knows when to fire). Providers read only the pinned
/// published <see cref="ExecutableNode"/>; a node whose recurrence spec is not an authored literal throws, which
/// fails the publish rather than persisting an unfireable schedule.
/// </remarks>
public interface IRecurringTriggerScheduleProvider
{
    /// <summary>
    /// Returns the recurring-start schedule for <paramref name="node"/> if this provider recognizes its activity
    /// type; otherwise <c>null</c>.
    /// </summary>
    RecurringScheduleDescriptor? Describe(ExecutableNode node);
}
