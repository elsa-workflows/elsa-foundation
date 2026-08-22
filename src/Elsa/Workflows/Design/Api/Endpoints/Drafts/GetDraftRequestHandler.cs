using Elsa.Events.Core.Contracts;
using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Models;
using Elsa.Persistence.Core.Design;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations.Core;
using System.Text.Json;

namespace Elsa.Workflows.Design.Api.Endpoints.Drafts;

public sealed class GetDraftRequestHandler(IWorkflowDefinitionDraftStore draftStore)
    : IRequestHandler<GetDraft, WorkflowDraftView>
{
    public async Task<WorkflowDraftView> Handle(GetDraft request, CancellationToken cancellationToken)
    {
        var result = await draftStore.FindWithLayoutByIdAsync(request.DraftId, cancellationToken)
            ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinitionDraft), request.DraftId);
        return WorkflowDraftView.From(
            result.Draft,
            result.Layout,
            result.ActivityPresentation);
    }
}
