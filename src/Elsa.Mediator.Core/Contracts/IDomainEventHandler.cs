namespace Elsa.Mediator.Core.Contracts
{
    public interface IDomainEventHandler<TDomainEvent>
        where TDomainEvent : IDomainEvent
    {
        ValueTask Handle(TDomainEvent domainEvent, CancellationToken cancellationToken);
    }
}
