namespace Elsa.Events.Core.Contracts;

/// <summary>
/// Publishes events to their handlers. The single in-process publish mechanism — replaces the
/// former domain-event / notification / lifecycle-event senders.
/// </summary>
/// <remarks>
/// The optional <see cref="IEventPublishingStrategy"/> controls HOW handlers are dispatched.
/// When omitted, the configured default strategy is used (Sequential: synchronous, in-order,
/// awaited, and CAN break the caller). Pass an explicit strategy to override per call — e.g.
/// <c>EventPublishingStrategy.Background</c> for fire-and-forget.
/// <para>
/// Most call sites should prefer the intent-revealing <see cref="IInlineEventPublisher"/> (awaits
/// handler effects) or <see cref="IDeferredEventPublisher"/> (fire-and-forget) over selecting a
/// strategy here; those faces make the wrong delivery semantics unrepresentable at the dependency
/// level. This strategy-selecting surface is retained for callers that need explicit control.
/// </para>
/// </remarks>
public interface IEventPublisher
{
    Task Publish(IEvent @event, IEventPublishingStrategy? strategy = null, CancellationToken cancellationToken = default);
}
