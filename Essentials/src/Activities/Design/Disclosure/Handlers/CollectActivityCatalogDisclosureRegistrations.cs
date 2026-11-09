using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Events;
using Elsa.Events.Core.Contracts;

namespace Elsa.Activities.Design.Disclosure.Handlers;

/// <summary>The single fan-in handler; registry initialization reads these contributions back.</summary>
internal sealed class CollectActivityCatalogDisclosureRegistrations(
    IEnumerable<IActivityCatalogDisclosureRegistrationSource> sources)
    : IEventHandler<ActivityCatalogDisclosureInitializing>
{
    public Task Handle(ActivityCatalogDisclosureInitializing domainEvent, CancellationToken cancellationToken)
    {
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var registration in source.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                domainEvent.Registrations.Add(registration);
            }
        }
        return Task.CompletedTask;
    }
}
