using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Events;
using Elsa.Events.Core.Contracts;

namespace Elsa.Activities.Runtime.Handlers;

/// <summary>
/// The single <c>OnImplementationDescriptorsInitializing</c> handler. Resolves every registered
/// <see cref="IImplementationDescriptorSource"/> and adds their registrations to the event.
/// Modules contribute by registering an <see cref="IImplementationDescriptorSource"/>.
/// </summary>
public sealed class RegisterImplementationDescriptors(IEnumerable<IImplementationDescriptorSource> sources)
    : IEventHandler<OnImplementationDescriptorsInitializing>
{
    public Task Handle(OnImplementationDescriptorsInitializing domainEvent, CancellationToken cancellationToken)
    {
        foreach (var source in sources)
            foreach (var registration in source.GetRegistrations())
                domainEvent.Registrations.Add(registration);

        return Task.CompletedTask;
    }
}
