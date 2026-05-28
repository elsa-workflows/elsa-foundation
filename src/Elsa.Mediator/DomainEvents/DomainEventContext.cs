using Elsa.Mediator.Core.Contracts;

namespace Elsa.Mediator.DomainEvents;

public sealed record DomainEventContext(IDomainEvent Event, IServiceProvider ServiceProvider, CancellationToken CancellationToken) : IDomainEventContext
{
    public IDomainEventHandler? CurrentHandler { get; set; }
}
