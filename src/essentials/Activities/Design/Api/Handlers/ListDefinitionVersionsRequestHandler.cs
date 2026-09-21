using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Handlers;

public sealed class ListDefinitionVersionsRequestHandler(IActivityDefinitionVersionStore versionStore)
    : IRequestHandler<ListDefinitionVersions, IEnumerable<ActivityDefinitionVersionSummary>>
{
    public async Task<IEnumerable<ActivityDefinitionVersionSummary>> Handle(ListDefinitionVersions request, CancellationToken cancellationToken)
    {
        var versions = await versionStore.ListByDefinitionAsync(request.DefinitionId, cancellationToken);
        return versions.Select(e => new ActivityDefinitionVersionSummary(e.Id, e.Version, e.CreatedAt, e.ExecutionType)).ToArray();
    }
}
