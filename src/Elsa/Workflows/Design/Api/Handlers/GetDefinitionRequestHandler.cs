using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class GetDefinitionRequestHandler(
    IQueries<WorkflowDefinitionVersion> versionQueries,
    IQueries<WorkflowDefinition> defQueries,
    IQueries<WorkflowDefinitionDraft> draftQueries)
    : IRequestHandler<GetDefinition, WorkflowDefinitionDetailsView>
{
    public async Task<WorkflowDefinitionDetailsView> Handle(GetDefinition request, CancellationToken cancellationToken)
    {
        var versionFilter = new WorkflowDefinitionVersionFilter { DefinitionId = request.Id };

        var definitionTask = defQueries.Get(request.Id, cancellationToken);
        var versionsTask = versionQueries.Query(versionFilter, Constants.Expressions.VersionSelector, cancellationToken);
        var draftTask = draftQueries.Find(d => d.WorkflowDefinitionId == request.Id, cancellationToken);

        var definition = await definitionTask;
        var versions = await versionsTask;
        var draft = await draftTask;

        return new WorkflowDefinitionDetailsView(
            definition.ToView(),
            draft?.State.ToStateView(),
            versions
        );
    }
}
