using Elsa.Events.Core.Contracts;
using Elsa.Events.Strategies;
using Elsa.Locking.Core;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

public sealed class GroundworkUpdateDraftCommand(
    IDistributedLockProvider lockProvider,
    IDocumentStore store,
    IPayloadSerializer payloadSerializer,
    IEventPublisher eventPublisher,
    IDraftStateDiffEngine diffEngine,
    ISystemClock clock)
    : IUpdateDraftCommand
{
    public async Task Execute(UpdateDraftRequest request, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<IEvent> diffEvents;
        WorkflowDefinitionDraft draft;
        IReadOnlyList<ValidationError> errors;
        var documents = DraftDocuments();
        var lockKey = WorkflowDesignPersistenceLockKeys.DraftKey(request.DraftId);

        await using (var lockHandle = await lockProvider.AcquireLockAsync(lockKey, null, cancellationToken))
        {
            var document = await documents.FindByIdAsync(request.DraftId, cancellationToken)
                ?? throw new InvalidOperationException($"Workflow definition draft '{request.DraftId}' not found");

            draft = document.Entity;
            var storedState = draft.State;
            var storedLayout = document.Layout.ToArray();

            draft.State = request.State;
            diffEvents = diffEngine.Evaluate(request.DraftId, storedState, storedLayout, request.State, request.Layout);
            errors = await ExecuteValidationGate(draft, cancellationToken);
            GroundworkEntityTimestamps.StampModified(draft, clock.UtcNow);

            await store.SaveAllAsync(
                DocumentCommitScope.Of(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind),
                [documents.ToSaveRequest(draft, request.Layout.ToArray(), errors)],
                cancellationToken);
        }

        foreach (var diffEvent in diffEvents)
            await eventPublisher.Publish(diffEvent, EventPublishingStrategy.Background, cancellationToken);

        await eventPublisher.Publish(new OnDraftValidated(draft, errors), EventPublishingStrategy.Background, cancellationToken);
    }

    private async Task<IReadOnlyList<ValidationError>> ExecuteValidationGate(WorkflowDefinitionDraft draft, CancellationToken cancellationToken)
    {
        var validatingEvent = new OnDraftValidating(draft);
        await eventPublisher.Publish(validatingEvent, EventPublishingStrategy.Sequential, cancellationToken);
        return validatingEvent.Errors.ToArray();
    }

    private GroundworkWorkflowDefinitionDraftDocumentStore DraftDocuments() =>
        new(store, GroundworkDesignDocumentSerialization.Create(payloadSerializer));
}
