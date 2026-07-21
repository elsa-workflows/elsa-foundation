using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Describes the recurring-start schedule a Timer/Cron start-trigger activity node declares, at publish time
/// (W16). Providers form an exact-one Strategy set selected by executable-node context: the schedule indexer
/// asks every provider to describe each recurring trigger node and fails preflight if more than one recognizes
/// it. Returning <c>null</c> means "not my activity type".
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
    /// Gets the stable, non-secret identity used to attribute recurring preflight outcomes and failures.
    /// Existing providers receive a deterministic fallback derived from their public CLR type identity. An
    /// override must be nonblank whenever the provider recognizes a node or fails while describing one.
    /// </summary>
    string ProviderId => GetType().FullName ?? GetType().Name;

    /// <summary>
    /// Returns the recurring-start schedules for <paramref name="node"/> when this provider recognizes its
    /// activity type; otherwise an empty collection. A single-schedule trigger (<c>Timer</c>/<c>Cron</c>) returns
    /// one descriptor; a composite start-trigger activity may fan out one descriptor per authored recurring start
    /// element (spec 117 — a BPMN process with several timer start events). An empty collection means "no
    /// contribution" — either not this provider's activity type, or a recognized node that declares no recurring
    /// starts — and never fails the publish on its own.
    /// </summary>
    IReadOnlyCollection<RecurringScheduleDescriptor> Describe(ExecutableNode node);
}
