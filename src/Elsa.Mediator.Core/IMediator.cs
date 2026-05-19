namespace Elsa.Mediator.Core
{
    public interface IMediator
    {
        Task Publish<TDomainEvent>(TDomainEvent domainEvent, CancellationToken cancellationToken)
            where TDomainEvent : IDomainEvent;
    }
}
