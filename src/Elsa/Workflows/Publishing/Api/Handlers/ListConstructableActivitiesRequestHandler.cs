using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;

namespace Elsa.Workflows.Publishing.Api.Handlers;

/// <summary>
/// Reads the catalog (the Design seam) and projects each row to its identity + descriptor kind.
/// No construction happens here — this is the "what could I build?" half of the bridge.
/// </summary>
public sealed class ListConstructableActivitiesRequestHandler(IActivityDefinitionVersionStore versions)
    : IRequestHandler<ListConstructableActivities, IEnumerable<ConstructableActivityView>>
{
    public async Task<IEnumerable<ConstructableActivityView>> Handle(ListConstructableActivities request, CancellationToken cancellationToken)
    {
        var rows = await versions.ListAsync(cancellationToken);

        return rows
            .Where(v => request.DescriptorType is null || v.DescriptorType == request.DescriptorType)
            .Select(v => new ConstructableActivityView(v.Id, v.DefinitionId, v.Version, v.DescriptorType))
            .ToArray();
    }
}
