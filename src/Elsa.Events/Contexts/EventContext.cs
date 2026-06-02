using Elsa.Events.Core.Contracts;

namespace Elsa.Events.Contexts;

/// <inheritdoc cref="IEventContext" />
public sealed class EventContext(
    IEvent @event,
    IEventPublishingStrategy strategy,
    IServiceProvider serviceProvider,
    CancellationToken cancellationToken = default
)
    : IEventContext
{
    public IEvent Event { get; init; } = @event;
    public IEventPublishingStrategy Strategy { get; init; } = strategy;
    public IServiceProvider ServiceProvider { get; } = serviceProvider;
    public CancellationToken CancellationToken { get; init; } = cancellationToken;
}
