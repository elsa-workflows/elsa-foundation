using Elsa.Activities.Design.Core.Models;
using Elsa.Events.Core.Contracts;

namespace Elsa.Activities.Design.Core.Events;

/// <summary>Sequential startup fan-in; one aggregator collects registration-source metadata.</summary>
public sealed class ActivityCatalogDisclosureInitializing : IEvent
{
    public ICollection<CatalogDisclosureRegistration> Registrations { get; } = [];
}
