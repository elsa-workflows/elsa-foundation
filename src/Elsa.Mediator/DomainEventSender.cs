using Elsa.Mediator.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Mediator;

public sealed class DomainEventSender(IServiceProvider serviceProvider) : IDomainEventSender
{
    public async Task Send<TDomainEvent>(TDomainEvent domainEvent, CancellationToken cancellationToken) 
        where TDomainEvent : IDomainEvent
    {
        var handlers = serviceProvider.GetServices<IDomainEventHandler<TDomainEvent>>();

        foreach (var handler in handlers)
            await handler.Handle(domainEvent, cancellationToken);
    }
}
