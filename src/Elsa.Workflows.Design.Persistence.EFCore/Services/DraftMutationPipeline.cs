using Elsa.Locking.Core;
using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EFCore.Constants;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EFCore.Services;

/// <summary>
/// Shared pipeline that wraps every Draft mutation per Unit C FR-027.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order of operations.</b>
/// <list type="number">
/// <item>Acquire <c>workflow-draft:{DraftId}</c> distributed lock.</item>
/// <item>Load Draft; invoke loading handlers to hydrate <c>State</c> from <c>StateSource</c>.</item>
/// <item>Run the hook — closure mutates State + returns the granular <see cref="ILifecycleEvent"/>.</item>
/// <item>Mark the Draft entity <c>Modified</c> (State is <c>[NotMapped]</c>; EF can't observe the mutation on its own).</item>
/// <item><b>Synchronously</b> publish <c>OnDraftValidating</c> as a domain event — validators
///       contribute errors via <c>AddValidationError(...)</c>. This is the gate.</item>
/// <item>Upsert the <see cref="WorkflowDefinitionDraftValidation"/> sibling with the
///       collected errors (delete-and-re-add per FR-023 — rewritten wholesale on every pass).</item>
/// <item><c>SaveChangesAsync</c> — state + validation sibling persist atomically.</item>
/// <item>Release the per-Draft lock.</item>
/// <item><b>Background</b> publish the granular FR-018 / FR-018a lifecycle event — "mutation
///       happened" (cause).</item>
/// <item><b>Background</b> publish <c>OnDraftValidated</c> — "validation happened, here's the
///       outcome" (consequence). Fires AFTER the granular event to preserve cause-effect
///       order in the lifecycle stream.</item>
/// </list>
/// </para>
/// <para>
/// <b>Two senders, two roles.</b>
/// <list type="bullet">
/// <item><see cref="IDomainEventSender"/> — for the synchronous gate <c>OnDraftValidating</c>.
///       Contribution pattern; publisher awaits + reads errors back (§2.6.1).</item>
/// <item><see cref="ILifecycleEventSender"/> — for the post-lock-release lifecycle events
///       (granular mutation + validation outcome). Notification pattern; publisher fires +
///       returns; subscribers run later on the background worker (§2.6.6).</item>
/// </list>
/// </para>
/// <para>
/// <b>Why this order.</b> The user's mutation IS the lifecycle transition. State + validation
/// errors are persisted together inside the lock so a single transaction succeeds or fails as
/// one (this is what makes the promotion gate — FR-024 — well-defined: a Version can only be
/// minted from a Draft whose persisted validation row has empty <c>Errors</c>). Audit /
/// event-sourcing / telemetry subscribers run async — they don't affect the response latency
/// or the persistence transaction. <c>OnDraftValidated</c> fires AFTER the granular event so
/// cause-before-effect ordering is preserved across the lifecycle stream.
/// </para>
/// </remarks>
public sealed class DraftMutationPipeline(
    IDistributedLockProvider lockProvider,
    IDbContextFactory<WorkflowsDesignDbContext> contextFactory,
    IDomainEventSender domainEventSender,
    ILifecycleEventSender lifecycleEventSender,
    IIdentityGenerator identityGenerator,
    IEnumerable<IEntityLoadingHandler<WorkflowsDesignDbContext, WorkflowDefinitionDraft>> draftLoadingHandlers
)
{
    public async Task ExecuteMutation(
        string draftId,
        Func<WorkflowDefinitionDraft, WorkflowsDesignDbContext, ValueTask<ILifecycleEvent>> mutateDelegate,
        CancellationToken cancellationToken = default
    )
    {
        ILifecycleEvent granularEvent;
        WorkflowDefinitionDraft draft;
        IReadOnlyList<ValidationError> errors;

        var lockKey = LockKeys.DraftKey(draftId);

        await using (var lockHandle = await lockProvider.AcquireLockAsync(lockKey, null, cancellationToken))
        await using (var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            draft = await LoadAndHydrate(dbContext, draftId, cancellationToken);

            granularEvent = await mutateDelegate(draft, dbContext);

            // State is [NotMapped]; force-mark Modified so the saving handler re-serialises
            // State into StateSource before SaveChangesAsync.
            dbContext.Entry(draft).State = EntityState.Modified;

            // Validation runs as part of the transaction: validators contribute, errors
            // upsert into the sibling, SaveChanges flushes everything together.
            errors = await ExecuteValidationGate(draft, dbContext, cancellationToken);
        }

        // Lock released, transaction committed — fire the lifecycle stream in cause-effect
        // order: granular mutation event first (the cause), validation outcome second
        // (the consequence).
        await PublishLifecycleEvents(granularEvent, draft, errors, cancellationToken);
    }

    public async Task ExecuteCreation(
        WorkflowDefinitionDraft newDraft,
        WorkflowDefinitionDraftLayout? newLayout,
        ILifecycleEvent originationEvent,
        CancellationToken cancellationToken = default
    )
    {
        IReadOnlyList<ValidationError> errors;

        var lockKey = LockKeys.DraftKey(newDraft.Id);

        await using (var lockHandle = await lockProvider.AcquireLockAsync(lockKey, null, cancellationToken))
        await using (var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            await dbContext.WorkflowDefinitionDrafts.AddAsync(newDraft, cancellationToken);

            if (newLayout is not null)
                await dbContext.WorkflowDefinitionDraftLayouts.AddAsync(newLayout, cancellationToken);

            errors = await ExecuteValidationGate(newDraft, dbContext, cancellationToken);
        }

        await PublishLifecycleEvents(originationEvent, newDraft, errors, cancellationToken);
    }

    private async Task<WorkflowDefinitionDraft> LoadAndHydrate(
        WorkflowsDesignDbContext dbContext,
        string draftId,
        CancellationToken cancellationToken
    )
    {
        var draft = await dbContext.WorkflowDefinitionDrafts.FirstOrDefaultAsync(d => d.Id == draftId, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow definition draft '{draftId}' not found");

        // Loading handlers hydrate State from the StateSource shadow JSON. EFCoreQueries
        // invokes them automatically; the pipeline uses a direct DbContext query (so the
        // lock + the context share a lifetime), and invokes them manually here.
        foreach (var handler in draftLoadingHandlers)
            await handler.Handle(dbContext, draft, cancellationToken);

        return draft;
    }

    /// <summary>
    /// Runs the synchronous validation gate (<see cref="OnDraftValidating"/>), upserts the
    /// errors into the validation sibling, flushes the transaction, and returns the persisted
    /// error set for the caller to surface on <see cref="OnDraftValidated"/>.
    /// </summary>
    private async Task<IReadOnlyList<ValidationError>> ExecuteValidationGate(
        WorkflowDefinitionDraft draft,
        WorkflowsDesignDbContext dbContext,
        CancellationToken cancellationToken
    )
    {
        var validatingEvent = new OnDraftValidating(draft);
        await domainEventSender.Send(validatingEvent, cancellationToken);

        var errors = validatingEvent.Errors.ToArray();

        await UpsertValidationSibling(draft.Id, errors, dbContext, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return errors;
    }

    /// <summary>
    /// FR-023 delete-and-re-add semantics. If a sibling row exists for the Draft, replace its
    /// <c>Errors</c> wholesale (EF tracks the change via the JSON-column <c>ValueComparer</c>).
    /// If no row exists yet (first validation pass after creation, or first mutation on a
    /// freshly-created Draft that never had a row), create one.
    /// </summary>
    private async Task UpsertValidationSibling(
        string draftId,
        IReadOnlyList<ValidationError> errors,
        WorkflowsDesignDbContext dbContext,
        CancellationToken cancellationToken
    )
    {
        var existing = await dbContext.WorkflowDefinitionDraftValidations
            .FirstOrDefaultAsync(v => v.WorkflowDefinitionDraftId == draftId, cancellationToken);

        if (existing is not null)
        {
            existing.Errors = [.. errors];
            return;
        }

        var sibling = new WorkflowDefinitionDraftValidation
        {
            Id = identityGenerator.Generate(),
            WorkflowDefinitionDraftId = draftId,
            Errors = [.. errors],
        };

        await dbContext.WorkflowDefinitionDraftValidations.AddAsync(sibling, cancellationToken);
    }

    private async Task PublishLifecycleEvents(
        ILifecycleEvent granularEvent,
        WorkflowDefinitionDraft draft,
        IReadOnlyList<ValidationError> errors,
        CancellationToken cancellationToken
    )
    {
        // Cause first.
        await lifecycleEventSender.Send(granularEvent, cancellationToken);

        // Consequence second.
        var validatedEvent = new DraftValidated(draft, errors);
        await lifecycleEventSender.Send(validatedEvent, cancellationToken);
    }    
}
