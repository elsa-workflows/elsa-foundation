using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Events;
using Elsa.Events.Core.Contracts;
using Elsa.Tasks.Core;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Publishes <c>OnActivityImplementationResolversInitializing</c> with an empty list;
/// modules handle the event to add their resolvers; flushes contributions into the registry.
/// Canonical §2.6.1 Registry + StartUp Task sub-pattern.
/// </summary>
public sealed class ActivityImplementationResolverRegistryStartupTask(
    IEventPublisher sender,
    IActivityImplementationResolverRegistry registry)
    : IStartupTask
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var resolvers = new List<IActivityImplementationResolver>();
        await sender.Publish(new OnActivityImplementationResolversInitializing(resolvers), cancellationToken: cancellationToken);
        registry.RegisterAll(resolvers);
    }
}
