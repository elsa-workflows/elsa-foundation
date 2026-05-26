using Elsa.Mediator.Core.Contracts;

namespace Elsa.Mediator.DomainEvents;

public sealed class DomainEventSender(IDomainEventPipeline requestPipeline, IServiceProvider serviceProvider) : IDomainEventSender
{
    public async Task Send(IDomainEvent @event, CancellationToken cancellationToken = default)
    {
        var context = new DomainEventContext(@event, serviceProvider, cancellationToken);
        await requestPipeline.Execute(context);
    }
}
