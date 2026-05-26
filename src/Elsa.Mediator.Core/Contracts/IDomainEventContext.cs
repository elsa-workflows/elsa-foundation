namespace Elsa.Mediator.Core.Contracts;

public interface IDomainEventContext
{
    IServiceProvider ServiceProvider { get; }

    IDomainEvent Event { get; }

    CancellationToken CancellationToken { get; }
}
