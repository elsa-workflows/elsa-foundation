using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class ListDefinitionVersionsRequestHandler(IQueries<WorkflowDefinitionVersion> queries)
    : IRequestHandler<ListDefinitionVersions, IEnumerable<WorkflowDefinitionVersionInfo>>
{
    public async Task<IEnumerable<WorkflowDefinitionVersionInfo>> Handle(ListDefinitionVersions request, CancellationToken cancellationToken)
    {
        var filter = new WorkflowDefinitionVersionFilter
        {
            DefinitionId = request.DefinitionId
        };

        return await queries.Query(filter, Constants.Expressions.VersionSelector, cancellationToken);
    }
}
