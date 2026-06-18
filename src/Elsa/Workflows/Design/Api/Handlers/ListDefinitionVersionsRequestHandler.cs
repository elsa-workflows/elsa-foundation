using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class ListDefinitionVersionsRequestHandler(IWorkflowDefinitionVersionStore store)
    : IRequestHandler<ListDefinitionVersions, IEnumerable<WorkflowDefinitionVersionInfo>>
{
    public async Task<IEnumerable<WorkflowDefinitionVersionInfo>> Handle(ListDefinitionVersions request, CancellationToken cancellationToken)
    {
        var versions = await store.ListByDefinitionAsync(request.DefinitionId, cancellationToken);
        return versions.Select(e => new WorkflowDefinitionVersionInfo(e.Id, e.Version, e.CreatedAt));
    }
}
