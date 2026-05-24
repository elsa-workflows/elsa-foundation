using Elsa.Mapping.Core.Contracts;
using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Extensions;
using Elsa.Workflows.Design.Persistence.Core.Filters;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class GetDefinitionRequestHandler(IQueries<WorkflowDefinitionVersion> versionQueries, IQueries<WorkflowDefinition> defQueries, IObjectMapper mapper)
    : IRequestHandler<GetDefinition, WorkflowDefinitionDetailsView>
{
    public async Task<WorkflowDefinitionDetailsView> Handle(GetDefinition request, CancellationToken cancellationToken)
    {
        var filter = new WorkflowDefinitionVersionFilter { DefinitionId = request.Id };

        var definitionTask = defQueries.GetDefinitionInlcudingDraft(request.Id, cancellationToken);
        var versionsTask = versionQueries.Query(filter, Constants.Expressions.VersionSelector, cancellationToken);

        var definition = await definitionTask;
        var versions = await versionsTask;

        var mappedDraft = definition.Draft is not null
            ? mapper.Map<WorkflowDefinitionStateView>(definition.Draft)
            : null;

        return new WorkflowDefinitionDetailsView(
            mapper.Map<WorkflowDefinitionView>(definition),
            mappedDraft,
            versions
        );
    }
}
