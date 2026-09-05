using Elsa.Events.Core.Contracts;
using Elsa.Locking.Core;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Core.Design;
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
        GroundworkDesignDeleteRequest? delete = null;
        DiscardDraftResult? resolvedResult = null;
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
                    var acceptedResult = resolvedResult ?? throw new InvalidOperationException(
                        "Workflow definition draft resolution did not complete before staging.");
                    if (delete is not null)
                        await context.DeleteAsync(delete, token);
                    return acceptedResult;
                },
                cancellationToken: cancellationToken,
                beforeAttempt: async token =>
                {
                    var document = await documents.FindByIdAsync(draftId, token);
                    if (document is null)
                    {
                        resolvedResult = new DiscardDraftResult(draftId, null, false);
                        return;
                    }

                    delete = documents.ToDeleteRequest(draftId, document.Version);
                    resolvedResult = new DiscardDraftResult(
                        draftId,
                        document.Entity.WorkflowDefinitionId,
                        true);
                });
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
