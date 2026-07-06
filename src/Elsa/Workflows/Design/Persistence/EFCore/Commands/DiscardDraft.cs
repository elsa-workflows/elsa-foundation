using Elsa.Locking.Core;
using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

/// <summary>
/// Unit C FR-029 implementation. Removes a <c>WorkflowDefinitionDraft</c> + its
/// <c>WorkflowDefinitionDraftLayout</c> sibling row atomically inside the per-Draft
/// distributed lock. Versions are never touched.
/// </summary>
/// <remarks>
/// Discard is its own shape (deletion, no validation rebuild, no granular event) and shares no
/// flow with create or update. The lock + lifecycle-event semantics are preserved by invoking
/// <see cref="IDistributedLockProvider"/> and <see cref="IDeferredEventPublisher"/> directly.
/// </remarks>
public sealed class DiscardDraft(
    IDistributedLockProvider lockProvider,
    IDbContextFactory<WorkflowsDesignDbContext> contextFactory,
    IDeferredEventPublisher deferredEventPublisher
) : IDiscardDraftCommand
{
    public async Task Execute(string draftId, CancellationToken cancellationToken = default)
    {
        var lockKey = WorkflowDesignPersistenceLockKeys.DraftKey(draftId);

        string workflowDefinitionId;

        await using (var lockHandle = await lockProvider.AcquireLockAsync(lockKey, null, cancellationToken))
        await using (var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            var draft = await dbContext.WorkflowDefinitionDrafts
                .FirstOrDefaultAsync(d => d.Id == draftId, cancellationToken);

            if (draft is null)
                return; // Idempotent — second Discard on the same id is a no-op.

            workflowDefinitionId = draft.WorkflowDefinitionId;

            // Cascade configured on the FK (R5) deletes the Layout sibling.
            dbContext.WorkflowDefinitionDrafts.Remove(draft);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        // Lock released + transaction committed — publish the terminal lifecycle event.
        // Background: fire-and-forget, the publisher must not be broken by a subscriber.
        await deferredEventPublisher.Publish(new OnDraftDiscarded(draftId, workflowDefinitionId), cancellationToken);
    }
}
