using Elsa.Events.Core.Contracts;
using Elsa.Events.Contexts;

namespace Elsa.Events.Services;

/// <summary>
/// The single in-process event publisher. Replaces the former domain-event, notification, and
/// lifecycle-event senders. Every caller passes the strategy the event is dispatched on.
/// </summary>
internal sealed class EventPublisher(
    IServiceProvider serviceProvider,
    IEventPipeline eventPipeline
)
    : IEventPublisher
{
    public Task Publish(
        IEvent @event,
        IEventPublishingStrategy strategy,
        CancellationToken cancellationToken = default
    )
    {
        var context = new EventContext(
            @event,
            strategy,
            serviceProvider,
            cancellationToken
        );

        return eventPipeline.ExecuteAsync(context);
    }
}
