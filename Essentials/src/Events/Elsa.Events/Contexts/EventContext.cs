using Elsa.Events.Core.Contracts;

namespace Elsa.Events.Contexts;

/// <inheritdoc cref="IEventContext" />
internal sealed class EventContext(
    IEvent @event,
    IEventPublishingStrategy strategy,
    IServiceProvider serviceProvider,
    CancellationToken cancellationToken = default
)
    : IEventContext
{
    public IEvent Event { get; init; } = @event;

    /// <summary>The strategy this dispatch uses, chosen by the delivery face the event was published through.</summary>
    public IEventPublishingStrategy Strategy { get; init; } = strategy;

    public IServiceProvider ServiceProvider { get; } = serviceProvider;
    public CancellationToken CancellationToken { get; init; } = cancellationToken;
}
