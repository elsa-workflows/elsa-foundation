using Elsa.Events.Core.Contracts;
using Elsa.Events.Strategies;

namespace Elsa.Events.Services;

/// <summary>
/// Inline delivery: a thin wrapper over <see cref="IEventPublisher"/> that always dispatches on the
/// <see cref="EventPublishingStrategy.Sequential"/> strategy — handlers run in order, awaited, and
/// their effects on the event are observable on return. The wrapper IS the whole implementation; it
/// owns no pipeline logic of its own (that lives in <see cref="IEventPublisher"/>).
/// </summary>
public sealed class InlineEventPublisher(IEventPublisher eventPublisher) : IInlineEventPublisher
{
    public Task Publish(IEvent @event, CancellationToken cancellationToken = default) =>
        eventPublisher.Publish(@event, EventPublishingStrategy.Sequential, cancellationToken);
}
