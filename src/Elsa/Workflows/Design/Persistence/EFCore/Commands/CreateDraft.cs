using Elsa.Events.Core.Contracts;
using Elsa.Locking.Core;
using Elsa.Persistence.Core.Design;
using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Elsa.Workflows.Design.Validations.Core;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

/// <summary>
/// The single origination path for Drafts. Owns the full creation flow inline (no separate
/// pipeline collaborator): acquire the per-Draft lock, add the Draft + its layout sibling, run
/// the in-lock validation gate, flush atomically, then publish <see cref="OnDraftCreated"/>
/// (cause) followed by <see cref="OnDraftValidated"/> (consequence) on the Background strategy.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order of operations.</b>
/// <list type="number">
/// <item>Acquire the <c>workflow-draft:{DraftId}</c> distributed lock.</item>
/// <item>Add the new Draft (and its layout sibling).</item>
/// <item><b>Sequential</b> publish <see cref="OnDraftValidating"/> — the in-lock validation gate
///       (see <see cref="DraftValidationGate"/>).</item>
/// <item><c>SaveChangesAsync</c> — the Draft + layout persist atomically.</item>
/// <item>Release the lock.</item>
/// <item><b>Background</b> publish <see cref="OnDraftCreated"/> (the cause), then
///       <see cref="OnDraftValidated"/> (the consequence) — cause-before-effect order.</item>
/// </list>
/// </para>
/// <para>
/// <c>ICloneDraftFromVersionCommand</c> delegates here, passing the source version's copied State
/// + layout and the source version id; the only thing that varies between a fresh create and a
/// clone is <see cref="WorkflowDefinitionDraft.SourceVersionId"/> (<c>null</c> for fresh, set for
/// a clone), surfaced on <see cref="OnDraftCreated.SourceVersionId"/>.
/// </para>
/// </remarks>
public sealed class CreateDraft(
    IIdentityGenerator identityGenerator,
    IDistributedLockProvider lockProvider,
    IDbContextFactory<WorkflowsDesignDbContext> contextFactory,
    IInlineEventPublisher inlineEventPublisher,
    IDeferredEventPublisher deferredEventPublisher
) : ICreateDraftCommand
{
    public async Task<string> Execute(
        DesignOperationKey operationKey,
        string workflowDefinitionId,
        WorkflowDefinitionState? initialState = null,
        IReadOnlyCollection<DesignMetadataRecord>? initialLayout = null,
        string? sourceVersionId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationKey);
        var draftId = identityGenerator.Generate();
        var state = initialState ?? new WorkflowDefinitionState(
            Variables: [],
            RootActivity: null,
            Inputs: [],
            Outputs: [],
            StrategyOptions: null
        );

        var draft = new WorkflowDefinitionDraft
        {
            Id = draftId,
            WorkflowDefinitionId = workflowDefinitionId,
            SourceVersionId = sourceVersionId,
            State = state,
        };

        var layout = WorkflowDefinitionDraftLayout.CreateFor(identityGenerator, draftId, initialLayout);

        IReadOnlyList<ValidationError> errors;

        var lockKey = WorkflowDesignPersistenceLockKeys.DraftKey(draftId);

        await using (var lockHandle = await lockProvider.AcquireLockAsync(lockKey, null, cancellationToken))
        await using (var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            await dbContext.WorkflowDefinitionDrafts.AddAsync(draft, cancellationToken);
            await dbContext.WorkflowDefinitionDraftLayouts.AddAsync(layout, cancellationToken);

            // In-lock validation gate (see DraftValidationGate); errors are derived, never persisted.
            errors = await inlineEventPublisher.DeriveValidationErrorsAsync(draft, cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        // Cause first. Deferred: fire-and-forget, subscribers must not break the publisher.
        await deferredEventPublisher.Publish(new OnDraftCreated(draftId, workflowDefinitionId, sourceVersionId), cancellationToken);

        // Consequence second.
        await deferredEventPublisher.Publish(new OnDraftValidated(draft, errors), cancellationToken);

        return draftId;
    }
}
