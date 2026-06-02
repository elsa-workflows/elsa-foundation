using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Events.Core.Contracts;

namespace Elsa.Activities.Runtime.Core.Events;

/// <summary>
/// Contribution event published once at startup by
/// <c>ActivityImplementationResolverRegistryStartupTask</c>. Each kind-owning module handles
/// this event and adds its resolver to the carried collection; the startup task flushes the
/// contributions into the registry.
/// </summary>
public sealed record OnActivityImplementationResolversInitializing(
    ICollection<IActivityImplementationResolver> Resolvers)
    : IEvent;
