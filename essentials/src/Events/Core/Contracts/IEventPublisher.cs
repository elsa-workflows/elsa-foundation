namespace Elsa.Events.Core.Contracts;

/// <summary>
/// Publishes events to their handlers. The single in-process publish mechanism — replaces the
/// former domain-event / notification / lifecycle-event senders.
/// </summary>
/// <remarks>
/// The <see cref="IEventPublishingStrategy"/> controls HOW handlers are dispatched. This interface is internal to the
/// Events feature: <see cref="IInlineEventPublisher"/> (Sequential, awaits handler effects) and
/// <see cref="IDeferredEventPublisher"/> (Background, fire-and-forget) are built on it and are the public way to
/// publish, and the background worker drains queued events through it with handler isolation.
/// </remarks>
internal interface IEventPublisher
{
    Task Publish(IEvent @event, IEventPublishingStrategy strategy, CancellationToken cancellationToken = default);
}
