using Elsa.Events.Core.Contracts;
using Elsa.Events.Strategies;
using Elsa.Locking.Core;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EFCore.Constants;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
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
/// <item>Capture the stored State + layout, then wholesale-assign the desired State + layout
///       records (last-writer-wins — no version check, FR-022).</item>
/// <item>Diff stored vs desired → an ordered list of per-concept mutation events.</item>
/// <item>Mark the Draft <c>Modified</c> (State is <c>[NotMapped]</c>).</item>
/// <item><b>Sequential</b> publish <c>OnDraftValidating</c> — the in-lock validation gate.</item>
/// <item>Upsert the validation sibling (wholesale rewrite, FR-023).</item>
/// <item><c>SaveChangesAsync</c> — State + layout + validation sibling persist atomically.</item>
/// <item>Release the lock.</item>
/// <item><b>Background</b> publish each per-diff event (the causes), then <c>OnDraftValidated</c>
///       (the consequence) — cause-before-effect ordering across the stream.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class UpdateDraftCommand(
    IDistributedLockProvider lockProvider,
    IDbContextFactory<WorkflowsDesignDbContext> contextFactory,
    IEventPublisher eventPublisher,
    IIdentityGenerator identityGenerator,
    IEnumerable<IEntityLoadingHandler<WorkflowsDesignDbContext, WorkflowDefinitionDraft>> draftLoadingHandlers,
    DraftStateDiffer differ
) : IUpdateDraftCommand
{
    public async Task Execute(UpdateDraftRequest request, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<IEvent> diffEvents;
        WorkflowDefinitionDraft draft;
        IReadOnlyList<ValidationError> errors;

        var lockKey = LockKeys.DraftKey(request.DraftId);

        await using (var lockHandle = await lockProvider.AcquireLockAsync(lockKey, null, cancellationToken))
        await using (var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            draft = await LoadAndHydrate(dbContext, request.DraftId, cancellationToken);

            var layout = await dbContext.WorkflowDefinitionDraftLayouts
                .FirstOrDefaultAsync(l => l.WorkflowDefinitionDraftId == request.DraftId, cancellationToken);

            // Capture stored snapshots BEFORE the wholesale assignment (records are immutable,
            // so these references survive the reassignment).
            var storedState = draft.State;
            var storedLayout = layout?.Records.ToList() ?? [];

            // Wholesale assign the desired state + layout (last-writer-wins, FR-022).
            draft.State = request.State;
            if (layout is not null)
                layout.Records = [.. request.Layout];

            diffEvents = differ.Diff(request.DraftId, storedState, storedLayout, request.State, request.Layout);

            // State is [NotMapped]; force-mark Modified so the saving handler re-serialises it.
            dbContext.Entry(draft).State = EntityState.Modified;

            errors = await ExecuteValidationGate(draft, dbContext, cancellationToken);
        }

        // Lock released, transaction committed — fire the lifecycle stream in cause-effect order:
        // every per-diff mutation event first (the causes), the validation outcome last.
        await PublishLifecycleEvents(diffEvents, draft, errors, cancellationToken);
    }

    private async Task<WorkflowDefinitionDraft> LoadAndHydrate(
        WorkflowsDesignDbContext dbContext,
        string draftId,
        CancellationToken cancellationToken
    )
    {
        var draft = await dbContext.WorkflowDefinitionDrafts.FirstOrDefaultAsync(d => d.Id == draftId, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow definition draft '{draftId}' not found");

        foreach (var handler in draftLoadingHandlers)
            await handler.Handle(dbContext, draft, cancellationToken);

        return draft;
    }

    private async Task<IReadOnlyList<ValidationError>> ExecuteValidationGate(
        WorkflowDefinitionDraft draft,
        WorkflowsDesignDbContext dbContext,
        CancellationToken cancellationToken
    )
    {
        var validatingEvent = new OnDraftValidating(draft);
        // Sequential gate: the single ExecuteValidations handler runs every IDraftValidator and
        // aggregates their errors onto the event; the publisher awaits the chain and we read the
        // accumulated errors back (§2.6.1 contribution).
        await eventPublisher.Publish(validatingEvent, EventPublishingStrategy.Sequential, cancellationToken);

        var errors = validatingEvent.Errors.ToArray();

        await UpsertValidationSibling(draft.Id, errors, dbContext, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return errors;
    }

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
        IReadOnlyList<IEvent> diffEvents,
        WorkflowDefinitionDraft draft,
        IReadOnlyList<ValidationError> errors,
        CancellationToken cancellationToken
    )
    {
        // Causes first: each per-diff mutation event, in the differ's deterministic order.
        // Background: fire-and-forget, subscribers must not break the command.
        foreach (var diffEvent in diffEvents)
            await eventPublisher.Publish(diffEvent, EventPublishingStrategy.Background, cancellationToken);

        // Consequence last.
        var validatedEvent = new OnDraftValidated(draft, errors);
        await eventPublisher.Publish(validatedEvent, EventPublishingStrategy.Background, cancellationToken);
    }
}
