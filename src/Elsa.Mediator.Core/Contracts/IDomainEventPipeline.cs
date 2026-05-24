namespace Elsa.Mediator.Core.Contracts;

public interface IDomainEventPipeline
{
    Task Execute(IDomainEventContext context);
}
