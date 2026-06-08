using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Extensions;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class GetVersionRequestHandler(IQueries<WorkflowDefinitionVersion> queries) : IRequestHandler<GetVersion, WorkflowDefinitionVersionDetailsView>
{
    public async Task<WorkflowDefinitionVersionDetailsView> Handle(GetVersion request, CancellationToken cancellationToken)
    {
        var result = await queries.GetVersionIncludingDefinition(request.VersionId, cancellationToken);
        return result.ToDetailsView();
    }
}
