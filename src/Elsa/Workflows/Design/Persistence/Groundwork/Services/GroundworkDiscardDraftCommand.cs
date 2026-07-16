using Elsa.Events.Core.Contracts;
using Elsa.Locking.Core;
using Elsa.Persistence.Core;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

public sealed class GroundworkDiscardDraftCommand(
    IDistributedLockProvider lockProvider,
    IDocumentStore store,
    IPayloadSerializer payloadSerializer,
    IDeferredEventPublisher deferredEventPublisher,
    IPersistenceAccessContextAccessor accessContextAccessor)
    : IDiscardDraftCommand
{
    public async Task Execute(string draftId, CancellationToken cancellationToken = default)
    {
        var documents = new GroundworkWorkflowDefinitionDraftDocumentStore(
            store,
            GroundworkDesignDocumentSerialization.Create(payloadSerializer),
            accessContextAccessor);
        var lockKey = WorkflowDesignPersistenceLockKeys.DraftKey(draftId);
        string workflowDefinitionId;

        await using (var lockHandle = await lockProvider.AcquireLockAsync(lockKey, null, cancellationToken))
        {
            var document = await documents.FindByIdAsync(draftId, cancellationToken);
            if (document is null)
                return;

            workflowDefinitionId = document.Entity.WorkflowDefinitionId;

            await store.DeleteAllAsync(
                DocumentCommitScope.Of(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind),
                [documents.ToDeleteRequest(draftId)],
                cancellationToken);
        }

        await deferredEventPublisher.Publish(new OnDraftDiscarded(draftId, workflowDefinitionId), cancellationToken);
    }
}
