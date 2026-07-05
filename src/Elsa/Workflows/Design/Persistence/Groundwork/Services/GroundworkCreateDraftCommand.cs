using Elsa.Events.Core.Contracts;
using Elsa.Events.Strategies;
using Elsa.Locking.Core;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Validations.Core;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

public sealed class GroundworkCreateDraftCommand(
    IIdentityGenerator identityGenerator,
    IDistributedLockProvider lockProvider,
    IDocumentStore store,
    IPayloadSerializer payloadSerializer,
    IEventPublisher eventPublisher,
    ISystemClock clock)
    : ICreateDraftCommand
{
    public async Task<string> Execute(
        string workflowDefinitionId,
        WorkflowDefinitionState? initialState = null,
        IReadOnlyCollection<DesignMetadataRecord>? initialLayout = null,
        string? sourceVersionId = null,
        CancellationToken cancellationToken = default)
    {
        var draftId = identityGenerator.Generate();
        var state = initialState ?? EmptyState();
        var layout = initialLayout?.ToArray() ?? [];
        var draft = new WorkflowDefinitionDraft
        {
            Id = draftId,
            WorkflowDefinitionId = workflowDefinitionId,
            SourceVersionId = sourceVersionId,
            State = state
        };

        IReadOnlyList<ValidationError> errors;
        var documents = DraftDocuments();
        var lockKey = WorkflowDesignPersistenceLockKeys.DraftKey(draftId);

        await using (var lockHandle = await lockProvider.AcquireLockAsync(lockKey, null, cancellationToken))
        {
            // In-lock validation gate (see DraftValidationGate); errors are derived, never persisted.
            errors = await eventPublisher.DeriveValidationErrorsAsync(draft, cancellationToken);
            GroundworkEntityTimestamps.StampAdded(draft, clock.UtcNow);

            await store.SaveAllAsync(
                DocumentCommitScope.Of(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind),
                [documents.ToSaveRequest(draft, layout)],
                cancellationToken);
        }

        await eventPublisher.Publish(new OnDraftCreated(draftId, workflowDefinitionId, sourceVersionId), EventPublishingStrategy.Background, cancellationToken);
        await eventPublisher.Publish(new OnDraftValidated(draft, errors), EventPublishingStrategy.Background, cancellationToken);

        return draftId;
    }

    private GroundworkWorkflowDefinitionDraftDocumentStore DraftDocuments() =>
        new(store, GroundworkDesignDocumentSerialization.Create(payloadSerializer));

    private static WorkflowDefinitionState EmptyState() => new(
        Variables: [],
        RootActivity: null,
        Inputs: [],
        Outputs: [],
        WorkflowActivityOptions: null,
        StrategyOptions: null);
}
