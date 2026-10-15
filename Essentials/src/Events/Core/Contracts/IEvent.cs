namespace Elsa.Events.Core.Contracts;

/// <summary>
/// Marker for an in-process event: "something happened." The single event concept in the
/// framework — it covers plain pub/sub, lifecycle/state-transition notifications, and the
/// contribution pattern (a handler mutates a shared payload the publisher reads back). The
/// axis that varies is how it is delivered, not the type: inline through
/// <see cref="IInlineEventPublisher"/> or deferred through <see cref="IDeferredEventPublisher"/>.
/// </summary>
/// <remarks>
/// Inline delivery is synchronous, in-order, and awaited: a throwing handler propagates to and
/// CAN break the caller. Deferred delivery returns before handlers run and catches and logs
/// their failures. There is no exception-shielding middleware on the inline path.
/// </remarks>
public interface IEvent
{
}
