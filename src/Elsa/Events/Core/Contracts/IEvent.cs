namespace Elsa.Events.Core.Contracts;

/// <summary>
/// Marker for an in-process event: "something happened." The single event concept in the
/// framework — it covers plain pub/sub, lifecycle/state-transition notifications, and the
/// contribution pattern (a handler mutates a shared payload the publisher reads back). The
/// axis that varies is the <see cref="IEventPublishingStrategy"/> the publisher selects, not
/// the type.
/// </summary>
/// <remarks>
/// Default publish (Sequential strategy) is synchronous, in-order, and awaited — a throwing
/// handler propagates to and CAN break the caller. Resilience is opt-in via a strategy that
/// owns it (e.g. Background fire-and-forget, which catches and logs). There is no
/// exception-shielding middleware on the default path.
/// </remarks>
public interface IEvent
{
}
