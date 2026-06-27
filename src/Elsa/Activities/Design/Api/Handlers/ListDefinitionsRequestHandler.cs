using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Projections;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Stores;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Handlers;

/// <summary>
/// Picker handler. Delegates the catalog query — including the reconciliation-state
/// visibility filter — to <see cref="IActivityDefinitionLookup"/>. Per spec FR-007 and SC-009,
/// the catalog store is the single source of truth; no live-provider enumeration path remains.
/// </summary>
public sealed class ListDefinitionsRequestHandler(
    IActivityDefinitionLookup lookup,
    IActivityAvailabilityEvaluator availabilityEvaluator,
    IActivityAvailabilitySettingsStore settingsStore)
    : IRequestHandler<ListDefinitions, IEnumerable<ActivityDefinitionView>>
{
    public async Task<IEnumerable<ActivityDefinitionView>> Handle(ListDefinitions request, CancellationToken cancellationToken)
    {
        var activities = await lookup.ListDefinitions(
            id: request.Id,
            category: request.Category,
            searchTerm: request.SearchTerm,
            displayName: request.DisplayName,
            description: request.Description,
            cancellationToken: cancellationToken);

        var settings = await settingsStore.LoadAsync(ActivityAvailabilitySettings.HostDefaultScope, cancellationToken);

        return availabilityEvaluator
            .FilterAddable(activities, settings)
            .Select(a => a.ToView())
            .ToArray();
    }
}
