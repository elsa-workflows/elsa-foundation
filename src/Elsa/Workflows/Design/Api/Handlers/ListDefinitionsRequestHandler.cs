using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class ListDefinitionsRequestHandler(IWorkflowDefinitionStore store)

    : IRequestHandler<ListDefinitions, IEnumerable<WorkflowDefinitionView>>
{
    public async Task<IEnumerable<WorkflowDefinitionView>> Handle(ListDefinitions request, CancellationToken cancellationToken)
    {
        var filter = new WorkflowDefinitionFilter
        {
            Id = request.Id,
            Description = request.Description,
            Name = request.Name,
            SearchTerm = request.SearchTerm,
            TenantAgnostic = request.TenantAgnostic
        };

        var definitions = await store.ListAsync(filter, cancellationToken);
        return definitions.Select(e => new WorkflowDefinitionView(e.Id, e.Name, e.Description, e.CreatedAt, e.LastModifiedAt));
    }
}
