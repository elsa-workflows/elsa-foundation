using Elsa.Activities.Design.Core.Models;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Core.Events;

/// <summary>
/// Contribution event published once at startup by
/// <c>ImplementationDescriptorRegistryStartupTask</c>. Each module that owns an
/// <c>ImplementationKind</c> handles this event and adds a registration to the carried
/// collection. The startup task flushes the contributions into the registry.
/// </summary>
public sealed record OnImplementationDescriptorsInitializing(
    ICollection<ImplementationDescriptorRegistration> Registrations)
    : IDomainEvent;
