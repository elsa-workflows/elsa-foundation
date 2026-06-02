using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Events;
using Elsa.Events.Core.Contracts;

namespace Elsa.Activities.Runtime.Handlers;

/// <summary>
/// The single <c>OnActivityImplementationResolversInitializing</c> handler. Resolves every
/// registered <see cref="IActivityImplementationResolverSource"/> and adds their resolvers to the
/// event. Modules contribute by registering an <see cref="IActivityImplementationResolverSource"/>.
/// </summary>
public sealed class RegisterActivityImplementationResolvers(IEnumerable<IActivityImplementationResolverSource> sources)
    : IEventHandler<OnActivityImplementationResolversInitializing>
{
    public Task Handle(OnActivityImplementationResolversInitializing domainEvent, CancellationToken cancellationToken)
    {
        foreach (var source in sources)
            foreach (var resolver in source.GetResolvers())
                domainEvent.Resolvers.Add(resolver);

        return Task.CompletedTask;
    }
}
