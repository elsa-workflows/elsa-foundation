using Elsa.Events.Core.Contracts;
using Elsa.Events.Strategies;
using Elsa.Locking.Core;
using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
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
/// <item><b>Sequential</b> publish <see cref="OnDraftValidating"/> — the in-lock validation gate;
///       the single <c>ExecuteValidations</c> handler aggregates every <c>IDraftValidator</c>'s
///       errors onto the event (§2.6.1 contribution). Errors are not persisted — they are
///       surfaced on <see cref="OnDraftValidated"/> and re-derived on demand.</item>
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
    IEventPublisher eventPublisher
) : ICreateDraftCommand
{
    public async Task<string> Execute(
        string workflowDefinitionId,
        WorkflowDefinitionState? initialState = null,
        IReadOnlyCollection<DesignMetadataRecord>? initialLayout = null,
        string? sourceVersionId = null,
        CancellationToken cancellationToken = default)
    {
        var draftId = identityGenerator.Generate();
        var state = initialState ?? new WorkflowDefinitionState(
            Variables: [],
            RootActivity: null,
            Inputs: [],
            Outputs: [],
            WorkflowActivityOptions: null,
            StrategyOptions: null
        );

        var draft = new WorkflowDefinitionDraft
        {
            Id = draftId,
            WorkflowDefinitionId = workflowDefinitionId,
            SourceVersionId = sourceVersionId,
            State = state,
        };

        var layout = new WorkflowDefinitionDraftLayout
        {
            Id = identityGenerator.Generate(),
            WorkflowDefinitionDraftId = draftId,
            Records = initialLayout is null ? [] : [.. initialLayout],
        };

        IReadOnlyList<ValidationError> errors;

        var lockKey = WorkflowDesignPersistenceLockKeys.DraftKey(draftId);

        await using (var lockHandle = await lockProvider.AcquireLockAsync(lockKey, null, cancellationToken))
        await using (var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            await dbContext.WorkflowDefinitionDrafts.AddAsync(draft, cancellationToken);
            await dbContext.WorkflowDefinitionDraftLayouts.AddAsync(layout, cancellationToken);

            errors = await ExecuteValidationGate(draft, dbContext, cancellationToken);
        }

        // Cause first. Background: fire-and-forget, subscribers must not break the publisher.
        await eventPublisher.Publish(new OnDraftCreated(draftId, workflowDefinitionId, sourceVersionId), EventPublishingStrategy.Background, cancellationToken);

        // Consequence second.
        await eventPublisher.Publish(new OnDraftValidated(draft, errors), EventPublishingStrategy.Background, cancellationToken);

        return draftId;
    }

    /// <summary>
    /// Runs the synchronous validation gate (<see cref="OnDraftValidating"/>), flushes the
    /// transaction, and returns the error set for the caller to surface on
    /// <see cref="OnDraftValidated"/>. Errors are not persisted — they are re-derived on demand.
    /// </summary>
    private async Task<IReadOnlyList<ValidationError>> ExecuteValidationGate(
        WorkflowDefinitionDraft draft,
        WorkflowsDesignDbContext dbContext,
        CancellationToken cancellationToken
    )
    {
        var validatingEvent = new OnDraftValidating(draft);
        // Sequential gate: the single ExecuteValidations handler runs every IDraftValidator and
        // aggregates their errors onto the event; the publisher awaits the full chain and reads
        // the accumulated errors back (§2.6.1 contribution).
        await eventPublisher.Publish(validatingEvent, EventPublishingStrategy.Sequential, cancellationToken);

        var errors = validatingEvent.Errors.ToArray();

        await dbContext.SaveChangesAsync(cancellationToken);

        return errors;
    }
}
