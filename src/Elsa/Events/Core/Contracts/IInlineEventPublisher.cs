namespace Elsa.Events.Core.Contracts;

/// <summary>
/// Publishes an event and awaits every handler before returning, so handler effects — errors
/// written onto the event, entities hydrated, contributions accumulated — are observable by the
/// caller on return. This is the delivery mode the read-back sites depend on (the draft
/// validation gate, entity saving/loading, expression-evaluation hooks).
/// </summary>
/// <remarks>
/// Depending on this contract (rather than on the general <see cref="IEventPublisher"/> plus a
/// caller-supplied strategy) makes deferred delivery unrepresentable at the dependency level: a
/// site that needs handler effects back cannot accidentally be handed a fire-and-forget publisher.
/// </remarks>
public interface IInlineEventPublisher
{
    Task Publish(IEvent @event, CancellationToken cancellationToken = default);
}
