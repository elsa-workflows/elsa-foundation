using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class ListDefinitionsRequestHandler(IQueries<WorkflowDefinition> queries)

    : IRequestHandler<ListDefinitions, IEnumerable<WorkflowDefinitionView>>
{
    public async Task<IEnumerable<WorkflowDefinitionView>> Handle(ListDefinitions request, CancellationToken cancellationToken)
    {
        var filter = new WorkflowDefinitionFilter
        {
            Id = request.Id,
            Description = request.Description,
            Name = request.Name,
            IsSystem = request.IsSystem,
            MaterializerName = request.MaterializerName,
            SearchTerm = request.SearchTerm,
            TenantAgnostic = request.TenantAgnostic
        };

        return await queries.Query(filter, Constants.Expressions.DefinitionSelector, cancellationToken);
    }
}
