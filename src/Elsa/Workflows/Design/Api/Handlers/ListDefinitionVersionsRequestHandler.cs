using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class ListDefinitionVersionsRequestHandler(IWorkflowDefinitionVersionStore store)
    : IRequestHandler<ListDefinitionVersions, IEnumerable<WorkflowDefinitionVersionSummary>>
{
    public async Task<IEnumerable<WorkflowDefinitionVersionSummary>> Handle(ListDefinitionVersions request, CancellationToken cancellationToken)
    {
        var versions = await store.ListByDefinitionAsync(request.DefinitionId, cancellationToken);
        return versions.Select(e => new WorkflowDefinitionVersionSummary(e.Id, e.Version, e.CreatedAt));
    }
}
