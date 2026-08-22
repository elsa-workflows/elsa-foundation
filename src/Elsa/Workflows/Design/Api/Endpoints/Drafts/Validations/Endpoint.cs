using Elsa.Api.AspNetCore;
using Elsa.Events.Core.Contracts;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations.Core;

namespace Elsa.Workflows.Design.Api.Endpoints.Drafts.Validations;

/// <summary>
/// Read path for the FR-024 validation error set. Loads the draft and derives its errors through the
/// single validation gate, using the shielded variant so a faulting validator surfaces as a
/// <c>Validation/Faulted</c> entry rather than turning the read into a 500.
/// </summary>
[Get("drafts/{draftId}/validations")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class Endpoint(
    IWorkflowDefinitionDraftStore draftStore,
    IInlineEventPublisher inlineEventPublisher) : ApiEndpoint<GetDraftValidations, DraftValidationsView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DraftsValidations";
        options.Accepts = ["*/*", "application/json"];
    }

    public override async Task<DraftValidationsView> HandleAsync(GetDraftValidations request, CancellationToken cancellationToken)
    {
        var draft = await draftStore.FindByIdAsync(request.DraftId, cancellationToken)
            ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinitionDraft), request.DraftId);
        var errors = await inlineEventPublisher.TryDeriveValidationErrorsAsync(draft, cancellationToken);
        return DraftValidationsView.From(request.DraftId, errors);
    }
}
