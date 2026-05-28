using Elsa.Activities.Runtime.Core.Events;
using Elsa.Activities.Runtime.Resolvers;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Runtime.Handlers;

/// <summary>
/// Contributes the CLR resolver to the runtime-side resolver registry.
/// </summary>
public sealed class ContributeClrResolver(ClrActivityImplementationResolver resolver)
    : IDomainEventHandler<OnActivityImplementationResolversInitializing>
{
    public ValueTask Handle(OnActivityImplementationResolversInitializing domainEvent, CancellationToken cancellationToken)
    {
        domainEvent.Resolvers.Add(resolver);
        return ValueTask.CompletedTask;
    }
}
