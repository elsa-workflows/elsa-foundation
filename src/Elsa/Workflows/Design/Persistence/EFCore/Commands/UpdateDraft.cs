using Elsa.Events.Core.Contracts;
using Elsa.Locking.Core;
using Elsa.Persistence.EFCore.Events;
using Elsa.Primitives.Contracts;
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
/// The single coarse Draft-mutation command (Unit 2). Owns its mutation shell inline (FR-007 —
/// no separate "pipeline" collaborator on the mutation path) and replaces the 20 granular
/// mutation commands.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order of operations</b> (data-model §5):
/// <list type="number">
/// <item>Acquire the per-Draft <c>workflow-draft:{DraftId}</c> distributed lock.</item>
/// <item>Load the Draft; hydrate <c>State</c> from <c>StateSource</c>; load the layout sibling.</item>
/// <item>Wholesale-assign the desired State + layout records (last-writer-wins — no version
///       check, FR-022).</item>
/// <item>Mark the Draft <c>Modified</c> (State is <c>[NotMapped]</c>).</item>
/// <item><b>Sequential</b> publish <c>OnDraftValidating</c> — the in-lock validation gate.</item>
/// <item><c>SaveChangesAsync</c> — State + layout persist atomically.</item>
/// <item>Release the lock.</item>
/// <item><b>Background</b> publish <c>OnDraftValidated</c> with the collected errors.</item>
/// </list>
/// </para>
/// <para>
/// Per-diff mutation events (the <c>On*InDraft</c>/<c>On*ToDraft</c> records produced by
/// <see cref="IDraftStateDiffEngine"/>) are no longer computed or published here: they had no
/// subscriber, and publication is retired until a real consumer (e.g. the event-sourcing slot,
/// spec 002 FR-017/FR-018) exists. The event records and the diff engine remain in place as the
/// tested contract to re-wire at that point.
/// </para>
/// <para>
/// Validation errors are not persisted either (spec 002 FR-021/FR-023 superseded): they are
/// recomputed by this gate on every mutation and re-derived by the promotion gate
/// (<see cref="IPromoteDraftToVersionCommand"/>) at promotion time.
/// </para>
/// </remarks>
public sealed class UpdateDraft(
    IIdentityGenerator identityGenerator,
    IDistributedLockProvider lockProvider,
    IDbContextFactory<WorkflowsDesignDbContext> contextFactory,
    IInlineEventPublisher inlineEventPublisher,
    IDeferredEventPublisher deferredEventPublisher
) : IUpdateDraftCommand
{
    public async Task Execute(UpdateDraftRequest request, CancellationToken cancellationToken = default)
    {
        WorkflowDefinitionDraft draft;
        IReadOnlyList<ValidationError> errors;

        var lockKey = WorkflowDesignPersistenceLockKeys.DraftKey(request.DraftId);

        await using (var lockHandle = await lockProvider.AcquireLockAsync(lockKey, null, cancellationToken))
        await using (var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            draft = await LoadAndHydrate(dbContext, request.DraftId, cancellationToken);

            var layout = await dbContext.WorkflowDefinitionDraftLayouts
                .FirstOrDefaultAsync(l => l.WorkflowDefinitionDraftId == request.DraftId, cancellationToken);

            // Wholesale assign the desired state (last-writer-wins, FR-022).
            draft.State = request.State;

            // Upsert the layout: a draft may have no layout row yet (older origination paths), in
            // which case create one so the submitted layout is not silently dropped.
            if (layout is null)
            {
                layout = WorkflowDefinitionDraftLayout.CreateFor(identityGenerator, request.DraftId, request.Layout);
                await dbContext.WorkflowDefinitionDraftLayouts.AddAsync(layout, cancellationToken);
            }
            else
            {
                layout.Records = [.. request.Layout];
            }

            // State is [NotMapped]; force-mark Modified so the saving handler re-serialises it.
            dbContext.Entry(draft).State = EntityState.Modified;

            // In-lock validation gate (see DraftValidationGate); errors are derived, never persisted.
            errors = await inlineEventPublisher.DeriveValidationErrorsAsync(draft, cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        // Lock released, transaction committed — surface the validation outcome.
        // Background: fire-and-forget, subscribers must not break the command.
        await deferredEventPublisher.Publish(new OnDraftValidated(draft, errors), cancellationToken);
    }

    private async Task<WorkflowDefinitionDraft> LoadAndHydrate(
        WorkflowsDesignDbContext dbContext,
        string draftId,
        CancellationToken cancellationToken
    )
    {
        var draft = await dbContext.WorkflowDefinitionDrafts.FirstOrDefaultAsync(d => d.Id == draftId, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow definition draft '{draftId}' not found");

        // Hydrate the already-tracked draft by publishing OnEntityLoading on the same context that
        // will SaveChangesAsync — the single ApplyEntityLoadingHandlers aggregator runs every
        // registered IEntityLoadingHandler<,>. Sequential so hydration completes before we read State.
        // (We deliberately do NOT route through a named read store, which returns an AsNoTracking
        // entity that this command could not save without re-attaching.)
        await inlineEventPublisher.Publish(new OnEntityLoading(dbContext, draft), cancellationToken);

        return draft;
    }
}
