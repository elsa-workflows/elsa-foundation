namespace Elsa.Mediator.Core.Contracts
{
    public interface IDomainEventHandler { }

    public interface IDomainEventHandler<TDomainEvent> : IDomainEventHandler
        where TDomainEvent : IDomainEvent
    {
        ValueTask Handle(TDomainEvent domainEvent, CancellationToken cancellationToken);
    }
}
