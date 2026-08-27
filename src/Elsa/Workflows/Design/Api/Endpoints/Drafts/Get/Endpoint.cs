using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using NativeEndpoints;

namespace Elsa.Workflows.Design.Api.Endpoints.Drafts.Get;

[Get("drafts/{draftId}")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class Endpoint(IWorkflowDefinitionDraftStore draftStore) : ApiEndpoint<GetDraft, WorkflowDraftView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DraftsGet";
        options.Accepts = ["*/*", "application/json"];
    }

    public override async Task<WorkflowDraftView> HandleAsync(GetDraft request, CancellationToken cancellationToken)
    {
        var result = await draftStore.FindWithLayoutByIdAsync(request.DraftId, cancellationToken)
            ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinitionDraft), request.DraftId);
        return WorkflowDraftView.From(
            result.Draft,
            result.Layout,
            result.ActivityPresentation);
    }
}
