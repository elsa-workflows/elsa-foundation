using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class GetDefinitionRequestHandler(
    IWorkflowDefinitionVersionStore versionStore,
    IWorkflowDefinitionStore definitionStore,
    IQueries<WorkflowDefinitionDraft> draftQueries)
    : IRequestHandler<GetDefinition, WorkflowDefinitionDetailsView>
{
    public async Task<WorkflowDefinitionDetailsView> Handle(GetDefinition request, CancellationToken cancellationToken)
    {
        var definitionTask = definitionStore.GetAsync(request.Id, cancellationToken);
        var versionsTask = versionStore.ListByDefinitionAsync(request.Id, cancellationToken);
        var draftTask = draftQueries.Find(d => d.WorkflowDefinitionId == request.Id, cancellationToken);

        var definition = await definitionTask;
        var versions = await versionsTask;
        var draft = await draftTask;

        return new WorkflowDefinitionDetailsView(
            definition.ToView(),
            draft?.State.ToStateView(),
            versions.Select(e => new WorkflowDefinitionVersionInfo(e.Id, e.Version, e.CreatedAt))
        );
    }
}
