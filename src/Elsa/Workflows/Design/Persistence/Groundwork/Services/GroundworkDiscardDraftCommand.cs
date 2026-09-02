using Elsa.Events.Core.Contracts;
using Elsa.Locking.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.Core.Contracts;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Services;

public sealed class GroundworkDiscardDraftCommand(
    IDistributedLockProvider lockProvider,
    GroundworkDesignStorage storage,
    IDesignAtomicWriter atomicWrite,
    IPayloadSerializer payloadSerializer,
    IDeferredEventPublisher deferredEventPublisher,
    IPersistenceAccessContextAccessor accessContextAccessor)
    : IDiscardDraftCommand
{
    private const string OperationKind = "workflow.draft.discard.v1";

    public async Task Execute(
        DesignOperationKey operationKey,
        string draftId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(draftId);
        var documents = new GroundworkWorkflowDefinitionDraftDocumentStore(
            storage,
            GroundworkDesignDocumentSerialization.Create(payloadSerializer),
            accessContextAccessor);
        var lockKey = WorkflowDesignPersistenceLockKeys.DraftKey(draftId);
        GroundworkDesignAtomicCommandResult<DiscardDraftResult> outcome;

        await using (var lockHandle = await lockProvider.AcquireLockAsync(lockKey, null, cancellationToken))
        {
            outcome = await GroundworkDesignAtomicCommand.ExecuteAsync(
                atomicWrite,
                operationKey,
                OperationKind,
                new DiscardDraftRequestMaterial(draftId),
                [WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind],
                async (context, token) =>
                {
                    var document = await documents.FindByIdAsync(draftId, token);
                    if (document is null)
                        return new DiscardDraftResult(draftId, null, false);
                    await context.DeleteAsync(documents.ToDeleteRequest(draftId, document.Version), token);
                    return new DiscardDraftResult(
                        draftId,
                        document.Entity.WorkflowDefinitionId,
                        true);
                },
                cancellationToken: cancellationToken);
        }

        if (outcome.ShouldPublishPostCommitOutcome &&
            outcome.Value is { WasDiscarded: true, WorkflowDefinitionId: { } workflowDefinitionId })
        {
            await deferredEventPublisher.Publish(
                new DraftDiscarded(draftId, workflowDefinitionId),
                CancellationToken.None);
        }
    }

    private sealed record DiscardDraftRequestMaterial(string DraftId);

    private sealed record DiscardDraftResult(
        string DraftId,
        string? WorkflowDefinitionId,
        bool WasDiscarded);
}
