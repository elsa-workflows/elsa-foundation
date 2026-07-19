using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class GetDefinitionRequestHandler(
    IWorkflowDefinitionVersionStore versionStore,
    IWorkflowDefinitionStore definitionStore,
    IWorkflowDefinitionDraftStore draftStore)
    : IRequestHandler<GetDefinition, WorkflowDefinitionDetailsView>
{
    public async Task<WorkflowDefinitionDetailsView> Handle(GetDefinition request, CancellationToken cancellationToken)
    {
        var definitionTask = definitionStore.GetAsync(request.DefinitionId, cancellationToken);
        var versionsTask = versionStore.ListByDefinitionAsync(request.DefinitionId, cancellationToken);
        var draftTask = draftStore.FindByWorkflowDefinitionIdAsync(request.DefinitionId, cancellationToken);

        var definition = await definitionTask;
        var versions = await versionsTask;
        var draft = await draftTask;
        var layout = draft is null
            ? []
            : await draftStore.FindLayoutByDraftIdAsync(draft.Id, cancellationToken);

        return new WorkflowDefinitionDetailsView(
            definition.ToView(),
            draft is null ? null : WorkflowDraftView.From(draft, layout),
            versions.Select(e => new WorkflowDefinitionVersionSummary(e.Id, e.Version, e.CreatedAt))
        );
    }
}
