using Elsa.Events.Core.Contracts;
using Elsa.Events.Contexts;

namespace Elsa.Events.Services;

/// <summary>
/// The single in-process event publisher. Replaces the former domain-event, notification, and
/// lifecycle-event senders. A publish with no explicit strategy uses
/// <paramref name="defaultPublishingStrategy"/> (Sequential by default — synchronous, awaited,
/// CAN break the caller). Fire-and-forget callers pass the Background strategy explicitly.
/// </summary>
public sealed class EventPublisher(
    IServiceProvider serviceProvider,
    IEventPublishingStrategy defaultPublishingStrategy,
    IEventPipeline eventPipeline
)
    : IEventPublisher
{
    public Task Publish(
        IEvent @event,
        IEventPublishingStrategy? strategy = null,
        CancellationToken cancellationToken = default
    )
    {
        var resolvedStrategy = strategy ?? defaultPublishingStrategy;

        var context = new EventContext(
            @event,
            resolvedStrategy,
            serviceProvider,
            cancellationToken
        );

        return eventPipeline.ExecuteAsync(context);
    }
}
