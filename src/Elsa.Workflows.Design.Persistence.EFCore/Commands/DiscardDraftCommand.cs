using Elsa.Locking.Core;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.EFCore.Constants;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

/// <summary>
/// Unit C FR-029 implementation. Removes a <c>WorkflowDefinitionDraft</c> + its sibling rows
/// (<c>WorkflowDefinitionDraftLayout</c>, <c>WorkflowDefinitionDraftValidation</c>)
/// atomically inside the per-Draft distributed lock. Versions are never touched.
/// </summary>
/// <remarks>
/// Does NOT route through <see cref="Services.DraftMutationPipeline"/> — that pipeline is
/// shaped for snapshot mutation + validation gate + lifecycle pair. Discard is a different
/// shape (deletion, no validation rebuild, no granular event); a custom flow keeps the
/// pipeline's hot path simple. The lock + lifecycle-event semantics are preserved by
/// invoking <see cref="IDistributedLockProvider"/> and <see cref="ILifecycleEventSender"/>
/// directly.
/// </remarks>
public sealed class DiscardDraftCommand(
    IDistributedLockProvider lockProvider,
    IDbContextFactory<WorkflowsDesignDbContext> contextFactory,
    ILifecycleEventSender lifecycleEventSender
) : IDiscardDraftCommand
{
    public async Task Execute(string draftId, CancellationToken cancellationToken = default)
    {
        var lockKey = LockKeys.DraftKey(draftId);

        string workflowDefinitionId;

        await using (var lockHandle = await lockProvider.AcquireLockAsync(lockKey, null, cancellationToken))
        await using (var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            var draft = await dbContext.WorkflowDefinitionDrafts
                .FirstOrDefaultAsync(d => d.Id == draftId, cancellationToken);

            if (draft is null)
                return; // Idempotent — second Discard on the same id is a no-op.

            workflowDefinitionId = draft.WorkflowDefinitionId;

            // Cascade configured on the FKs (R5) deletes the Layout + Validation siblings.
            dbContext.WorkflowDefinitionDrafts.Remove(draft);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        // Lock released + transaction committed — publish the terminal lifecycle event.
        await lifecycleEventSender.Send(new OnDraftDiscarded(draftId, workflowDefinitionId), cancellationToken);
    }
}
