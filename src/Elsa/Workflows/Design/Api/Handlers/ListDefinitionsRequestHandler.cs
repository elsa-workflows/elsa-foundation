using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class ListDefinitionsRequestHandler(
    IWorkflowDefinitionStore store,
    IWorkflowDefinitionListProjectionStore projectionStore)

    : IRequestHandler<ListDefinitions, WorkflowDefinitionListView>
{
    public async Task<WorkflowDefinitionListView> Handle(ListDefinitions request, CancellationToken cancellationToken)
    {
        var filter = new WorkflowDefinitionFilter
        {
            Id = request.Id,
            Description = request.Description,
            Name = request.Name,
            SearchTerm = request.SearchTerm,
            TenantAgnostic = request.TenantAgnostic
        };

        var definitions = (await store.ListAsync(filter, cancellationToken))
            .Where(definition => request.State?.ToLowerInvariant() switch
            {
                "deleted" => definition.DeletedAt is not null,
                "all" => true,
                _ => definition.DeletedAt is null
            })
            .ToArray();
        var items = await WorkflowDefinitionViewMapper.CreateAsync(definitions, projectionStore, cancellationToken);
        return new WorkflowDefinitionListView(items);
    }
}
