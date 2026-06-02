namespace Elsa.Events.Core.Contracts;

/// <summary>
/// Publishes events to their handlers. The single in-process publish mechanism — replaces the
/// former domain-event / notification / lifecycle-event senders.
/// </summary>
/// <remarks>
/// The optional <see cref="IEventPublishingStrategy"/> controls HOW handlers are dispatched.
/// When omitted, the configured default strategy is used (Sequential: synchronous, in-order,
/// awaited, and CAN break the caller). Pass an explicit strategy to override per call — e.g.
/// <c>EventPublishingStrategy.Background</c> for fire-and-forget, <c>Parallel</c> for awaited
/// fan-out.
/// </remarks>
public interface IEventPublisher
{
    Task Publish(IEvent @event, IEventPublishingStrategy? strategy = null, CancellationToken cancellationToken = default);
}
