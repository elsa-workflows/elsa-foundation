namespace Elsa.Mediator.Core.Contracts;

public interface IDomainEventMiddleware : IMiddleware
{
    ValueTask Invoke(IDomainEventContext context);
}
