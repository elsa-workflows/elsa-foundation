using Elsa.Activities.Design.Core.Events;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Runtime.Resolvers;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Runtime.Handlers;

/// <summary>
/// Contributes the (Kind, DescriptorType) mapping for CLR-backed activities to the
/// persistence-side descriptor registry. The kind value matches
/// <see cref="ClrActivityImplementationResolver.KindValue"/> and
/// <see cref="ClrImplementationDescriptor.Kind"/> — three places agree on the same string,
/// owned by this module.
/// </summary>
public sealed class ContributeClrDescriptorType
    : IDomainEventHandler<OnImplementationDescriptorsInitializing>
{
    public ValueTask Handle(OnImplementationDescriptorsInitializing domainEvent, CancellationToken cancellationToken)
    {
        domainEvent.Registrations.Add(new ImplementationDescriptorRegistration(
            ClrImplementationDescriptor.KindValue,
            typeof(ClrImplementationDescriptor))
        );
        return ValueTask.CompletedTask;
    }
}
