using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Extensions;
using Elsa.Mapping.Core.Contracts;
using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class GetVersionRequestHandler(IQueries<WorkflowDefinitionVersion> queries, IObjectMapper mapper) : IRequestHandler<GetVersion, WorkflowDefinitionVersionDetailsView>
{
    public async Task<WorkflowDefinitionVersionDetailsView> Handle(GetVersion request, CancellationToken cancellationToken)
    {
        var result = await queries.GetVersionIncludingDefinition(request.VersionId, cancellationToken);
        return mapper.Map<WorkflowDefinitionVersionDetailsView>(result);
    }
}
