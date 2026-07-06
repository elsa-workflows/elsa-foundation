using Elsa.Events.Core.Contracts;
using Elsa.Locking.Core;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Validations.Core;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

public sealed class GroundworkUpdateDraftCommand(
    IDistributedLockProvider lockProvider,
    IDocumentStore store,
    IPayloadSerializer payloadSerializer,
    IInlineEventPublisher inlineEventPublisher,
    IDeferredEventPublisher deferredEventPublisher,
    ISystemClock clock)
    : IUpdateDraftCommand
{
    public async Task Execute(UpdateDraftRequest request, CancellationToken cancellationToken = default)
    {
        WorkflowDefinitionDraft draft;
        IReadOnlyList<ValidationError> errors;
        var documents = DraftDocuments();
        var lockKey = WorkflowDesignPersistenceLockKeys.DraftKey(request.DraftId);

        await using (var lockHandle = await lockProvider.AcquireLockAsync(lockKey, null, cancellationToken))
        {
            var document = await documents.FindByIdAsync(request.DraftId, cancellationToken)
                ?? throw new InvalidOperationException($"Workflow definition draft '{request.DraftId}' not found");

            draft = document.Entity;

            // Wholesale assign the desired state (last-writer-wins, FR-022).
            draft.State = request.State;
            // In-lock validation gate (see DraftValidationGate); errors are derived, never persisted.
            errors = await inlineEventPublisher.DeriveValidationErrorsAsync(draft, cancellationToken);
            GroundworkEntityTimestamps.StampModified(draft, clock.UtcNow);

            await store.SaveAllAsync(
                DocumentCommitScope.Of(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind),
                [documents.ToSaveRequest(draft, request.Layout.ToArray())],
                cancellationToken);
        }

        await deferredEventPublisher.Publish(new OnDraftValidated(draft, errors), cancellationToken);
    }

    private GroundworkWorkflowDefinitionDraftDocumentStore DraftDocuments() =>
        new(store, GroundworkDesignDocumentSerialization.Create(payloadSerializer));
}
