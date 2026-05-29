namespace Elsa.Mediator.Core.Contracts;

/// <summary>
/// Marker for a <b>state-transition notification</b> emitted by a domain that manages a
/// lifecycle (e.g. a workflow draft's mutation lifecycle, a workflow instance's execution
/// lifecycle, an activity catalog's reconciliation lifecycle). Derives from
/// <see cref="INotification"/> — same characteristics for now (publisher doesn't read back,
/// subscribers observe but don't feed back), distinct marker so the two roles can diverge
/// without rewriting consumers.
/// </summary>
/// <remarks>
/// <b>Why a separate marker?</b> Notifications are general-purpose ("something happened —
/// react if you want"). Lifecycle events are specifically "a managed entity transitioned
/// state" — the audience is narrower (audit, event-sourcing stream, projection updaters,
/// telemetry) and the publisher's contract is stricter (the transition has ALREADY been
/// persisted by the time the event fires). Keeping them as separate markers lets each
/// domain's lifecycle dispatch evolve independently if needs diverge — without
/// re-classifying every notification call site.
/// </remarks>
public interface ILifecycleEvent : INotification
{
}
