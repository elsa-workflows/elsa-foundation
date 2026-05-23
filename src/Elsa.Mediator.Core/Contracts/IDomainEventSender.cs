namespace Elsa.Mediator.Core.Contracts
{
    public interface IDomainEventSender
    {
        Task Send(IDomainEvent domainEvent, CancellationToken cancellationToken);
    }
}
