using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Events;
using Elsa.Activities.Design.Core.Models;
using Elsa.Events.Core.Contracts;
using Elsa.Tasks.Core;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Persistence-side companion to the resolver registry startup task. Publishes
/// <c>OnImplementationDescriptorsInitializing</c>; modules handle the event to add
/// (Kind, DescriptorType) registrations; flushes into the registry.
/// </summary>
public sealed class ImplementationDescriptorRegistryStartupTask(
    IEventPublisher sender,
    IImplementationDescriptorRegistry registry)
    : IStartupTask
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var registrations = new List<ImplementationDescriptorRegistration>();
        await sender.Publish(new OnImplementationDescriptorsInitializing(registrations), cancellationToken: cancellationToken);
        registry.RegisterAll(registrations);
    }
}
