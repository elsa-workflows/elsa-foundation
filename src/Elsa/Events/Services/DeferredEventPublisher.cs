using Elsa.Events.Core.Contracts;
using Elsa.Events.Strategies;

namespace Elsa.Events.Services;

/// <summary>
/// Deferred delivery: a thin wrapper over <see cref="IEventPublisher"/> that always dispatches on the
/// <see cref="EventPublishingStrategy.Background"/> strategy — the event is enqueued and the call
/// returns before handlers run; the background worker drains the queue and shields handler failures.
/// The wrapper IS the whole implementation; it owns no pipeline logic of its own.
/// </summary>
public sealed class DeferredEventPublisher(IEventPublisher eventPublisher) : IDeferredEventPublisher
{
    public Task Publish(IEvent @event, CancellationToken cancellationToken = default) =>
        eventPublisher.Publish(@event, EventPublishingStrategy.Background, cancellationToken);
}
