namespace Elsa.Mediator.Core
{
    public interface IDomainEventHandler<TDomainEvent>
        where TDomainEvent : IDomainEvent
    {
        ValueTask Handle(TDomainEvent domainEvent, CancellationToken cancellationToken);
    }
}
