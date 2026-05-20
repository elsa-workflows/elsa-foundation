namespace Elsa.Mediator.Core.Contracts
{
    public interface IDomainEventSender
    {
        Task Send<TDomainEvent>(TDomainEvent domainEvent, CancellationToken cancellationToken)
            where TDomainEvent : IDomainEvent;
    }
}
