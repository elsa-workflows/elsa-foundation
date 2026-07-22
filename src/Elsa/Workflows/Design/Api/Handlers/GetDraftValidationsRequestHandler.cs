using Elsa.Events.Core.Contracts;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations.Core;

namespace Elsa.Workflows.Design.Api.Handlers;

/// <summary>
/// Read path for the FR-024 validation error set. Loads the draft and derives its errors through the
/// single validation gate, using the shielded variant so a faulting validator surfaces as a
/// <c>Validation/Faulted</c> entry rather than turning the read into a 500.
/// </summary>
public sealed class GetDraftValidationsRequestHandler(
    IWorkflowDefinitionDraftStore draftStore,
    IInlineEventPublisher inlineEventPublisher)
    : IRequestHandler<GetDraftValidations, DraftValidationsView>
{
    public async Task<DraftValidationsView> Handle(GetDraftValidations request, CancellationToken cancellationToken)
    {
        var draft = await draftStore.FindByIdAsync(request.DraftId, cancellationToken)
            ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinitionDraft), request.DraftId);
        var errors = await inlineEventPublisher.TryDeriveValidationErrorsAsync(draft, cancellationToken);
        return DraftValidationsView.From(request.DraftId, errors);
    }
}
