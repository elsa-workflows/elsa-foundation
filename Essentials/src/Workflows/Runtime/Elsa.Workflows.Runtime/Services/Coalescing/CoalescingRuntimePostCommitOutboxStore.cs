using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;

namespace Elsa.Workflows.Runtime.Services.Coalescing;

/// <summary>
/// Coalescing-aware overlay for <see cref="IRuntimePostCommitOutboxStore"/>. While a coalescing session owns the target
/// workflow execution, deliverable continuation intents are read from the session's overlay outbox and delivery results
/// recorded there, so intra-segment continuation runs from the working set until the flush. External intents and
/// source-authorized deliveries use the durable store's current claim lifecycle even while folding stays active.
/// Without a matching session, calls pass through to the durable inner store.
/// </summary>
public sealed class CoalescingRuntimePostCommitOutboxStore : IRuntimePostCommitOutboxStore, IPostCommitOutboxLookupStore, IRuntimePostCommitOutboxClaimStore, IRuntimePostCommitOutboxClaimCompletionStore
{
    private readonly IRuntimeCoalescingSessionAccessor sessionAccessor;
    private readonly IWorkflowExecutionStateStore workflowExecutionStateStore;
    private readonly HashSet<string> requiredSourceKinds;

    public CoalescingRuntimePostCommitOutboxStore(CoalescingInner<IRuntimePostCommitOutboxStore> inner,
        IRuntimeCoalescingSessionAccessor sessionAccessor, IWorkflowExecutionStateStore workflowExecutionStateStore)
        : this(inner, sessionAccessor, workflowExecutionStateStore, []) { }

    public CoalescingRuntimePostCommitOutboxStore(CoalescingInner<IRuntimePostCommitOutboxStore> inner,
        IRuntimeCoalescingSessionAccessor sessionAccessor, IWorkflowExecutionStateStore workflowExecutionStateStore,
        IEnumerable<RuntimePostCommitIntentHandlerContribution> contributions)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(sessionAccessor);
        ArgumentNullException.ThrowIfNull(workflowExecutionStateStore);
        ArgumentNullException.ThrowIfNull(contributions);
        this.sessionAccessor = sessionAccessor;
        this.workflowExecutionStateStore = workflowExecutionStateStore;
        _inner = inner.Value;
        _innerClaimStore = inner.Value as IRuntimePostCommitOutboxClaimStore;
        _innerCompletionStore = inner.Value as IRuntimePostCommitOutboxClaimCompletionStore;
        _innerLookupStore = inner.Value as IPostCommitOutboxLookupStore;
        requiredSourceKinds = RuntimePostCommitOutboxItems.RequiredSourceKinds(contributions);
    }

    private readonly IRuntimePostCommitOutboxStore _inner;
    private readonly IRuntimePostCommitOutboxClaimStore? _innerClaimStore;
    private readonly IRuntimePostCommitOutboxClaimCompletionStore? _innerCompletionStore;
    private readonly IPostCommitOutboxLookupStore? _innerLookupStore;

    public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetDeliverableAsync(RuntimePostCommitOutboxQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        if (query.WorkflowExecutionId is { } workflowExecutionId &&
            sessionAccessor.Current is { } session && session.AppliesTo(workflowExecutionId))
        {
            if (query.IntentKind is { } kind)
                return UsesOverlayKind(kind) ? new(session.GetDeliverableOutbox(query)) : _inner.GetDeliverableAsync(query, cancellationToken);
            return GetMixedDeliverableAsync(session, workflowExecutionId, query, cancellationToken);
        }
        return _inner.GetDeliverableAsync(query, cancellationToken);
    }

    private async ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetMixedDeliverableAsync(
        RuntimeCoalescingSession session, string workflowExecutionId, RuntimePostCommitOutboxQuery query, CancellationToken cancellationToken)
    {
        // Only owned scheduler continuations can run before the segment commits. External delivery reads the
        // actual durable lifecycle, which another scope may already have claimed or completed.
        var items = new List<RuntimePostCommitOutboxItem>();
        if (UsesOverlayKind(RuntimePostCommitIntentKinds.EnqueueSchedulerWork))
            items.AddRange(session.GetDeliverableOutbox(new(query.Now, query.Limit, workflowExecutionId,
                RuntimePostCommitIntentKinds.EnqueueSchedulerWork)).Where(IsOverlayContinuation));
        foreach (var durableKind in session.DurablyPersistedOutboxKinds().Where(kind => !UsesOverlayKind(kind)))
        {
            if (items.Count >= query.Limit) break;
            items.AddRange(await _inner.GetDeliverableAsync(new(query.Now, query.Limit - items.Count,
                workflowExecutionId, durableKind), cancellationToken));
        }
        return items;
    }

    private bool UsesOverlayKind(string kind) => kind == RuntimePostCommitIntentKinds.EnqueueSchedulerWork && !requiredSourceKinds.Contains(kind);
    private bool IsOverlayContinuation(RuntimePostCommitOutboxItem item) => UsesOverlayKind(item.Intent.Kind) && item.StimulusDeliverySourceScope is null;
    private bool OwnsOverlayContinuation(RuntimeCoalescingSession session, string id) =>
        session.TryFindOutboxItem(id, out var item) && IsOverlayContinuation(item!);

    public ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> RecordDeliveryResultAsync(RuntimePostCommitOutboxDeliveryResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (sessionAccessor.Current is { } session && session.IsActive && OwnsOverlayContinuation(session, result.OutboxItemId))
        {
            // An overlay item lives only in this session's working set and carries no durable claim, so no other
            // deliverer can hold it and supersession is not reachable on this branch.
            session.RecordOutboxDelivery(result);
            return new ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome>(
                RuntimePostCommitOutboxClaimCompletionOutcome.Persisted);
        }

        // Pass-through: the inner store's outcome is the answer. Never substitute Persisted here — that would report a
        // superseded item as delivered and inflate the drain's continuation signal.
        return _inner.RecordDeliveryResultAsync(result, cancellationToken);
    }

    public ValueTask<RuntimePostCommitOutboxItem?> FindAsync(
        string outboxItemId,
        CancellationToken cancellationToken = default) =>
        CoalescingPostCommitOutboxLookupStore.FindAsync(sessionAccessor, _innerLookupStore, outboxItemId, cancellationToken);

    public ValueTask<RuntimePostCommitOutboxItem?> FindDurableAsync(string outboxItemId, CancellationToken cancellationToken = default) =>
        (_innerLookupStore ?? throw new NotSupportedException("The inner provider cannot verify a durable outbox source."))
            .FindDurableAsync(outboxItemId, cancellationToken);

    public ValueTask<RuntimePostCommitOutboxItem?> FindUndeliveredDurableAsync(string workflowExecutionId, IReadOnlyCollection<string> excludedIntentKinds,
        CancellationToken cancellationToken = default) =>
        (_innerLookupStore ?? throw new NotSupportedException("The inner provider cannot verify prior durable outbox responsibility."))
            .FindUndeliveredDurableAsync(workflowExecutionId, excludedIntentKinds, cancellationToken);

    public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxClaim>> ClaimAsync(
        RuntimePostCommitOutboxClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.WorkflowExecutionId is { } workflowExecutionId &&
            sessionAccessor.Current is { } session && session.AppliesTo(workflowExecutionId))
        {
            if (request.IntentKind is { } kind)
                return UsesOverlayKind(kind) ? new(session.ClaimOutbox(request, IsOverlayContinuation))
                    : InnerClaims.ClaimAsync(request, cancellationToken);
            return ClaimMixedAsync(session, workflowExecutionId, request, cancellationToken);
        }
        return InnerClaims.ClaimAsync(request, cancellationToken);
    }

    private async ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxClaim>> ClaimMixedAsync(
        RuntimeCoalescingSession session, string workflowExecutionId, RuntimePostCommitOutboxClaimRequest request, CancellationToken cancellationToken)
    {
        var claims = new List<RuntimePostCommitOutboxClaim>();
        if (UsesOverlayKind(RuntimePostCommitIntentKinds.EnqueueSchedulerWork))
            claims.AddRange(session.ClaimOutbox(new(request.OwnerId, request.Now, request.VisibilityTimeout,
                request.Limit, workflowExecutionId, RuntimePostCommitIntentKinds.EnqueueSchedulerWork), IsOverlayContinuation));
        foreach (var durableKind in session.DurablyPersistedOutboxKinds().Where(kind => !UsesOverlayKind(kind)))
        {
            if (claims.Count >= request.Limit) break;
            // Sequential awaits: the inner stores may share one scoped EF context.
            claims.AddRange(await InnerClaims.ClaimAsync(new(request.OwnerId, request.Now, request.VisibilityTimeout,
                request.Limit - claims.Count, workflowExecutionId, durableKind), cancellationToken));
        }
        return claims;
    }

    private IRuntimePostCommitOutboxClaimStore InnerClaims => _innerClaimStore ?? throw new InvalidOperationException(
        "The configured post-commit outbox store does not provide atomic claim support.");

    public ValueTask<RuntimePostCommitOutboxClaim?> RenewClaimAsync(
        RuntimePostCommitOutboxClaim claim,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);

        if (sessionAccessor.Current is { } session && session.IsActive && OwnsOverlayContinuation(session, claim.OutboxItemId))
            return new ValueTask<RuntimePostCommitOutboxClaim?>(session.RenewOutboxClaim(claim, now, visibilityTimeout));

        return (_innerClaimStore ?? throw new InvalidOperationException(
            "The configured post-commit outbox store does not provide atomic claim support."))
            .RenewClaimAsync(claim, now, visibilityTimeout, cancellationToken);
    }

    // Always the durable store: an overlay item lives only in this session's working set, so only the drain that owns the
    // session ever claims it, and that drain completes its claims before it asks who else holds its continuations.
    public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> ListClaimedAsync(
        RuntimePostCommitOutboxClaimedQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return (_innerClaimStore ?? throw new InvalidOperationException(
            "The configured post-commit outbox store does not provide atomic claim support."))
            .ListClaimedAsync(query, cancellationToken);
    }

    public ValueTask RecordDeliveryResultAsync(
        RuntimePostCommitOutboxClaim claim,
        RuntimePostCommitOutboxDeliveryResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(result);

        if (sessionAccessor.Current is { } session && session.IsActive && OwnsOverlayContinuation(session, claim.OutboxItemId))
        {
            session.RecordOutboxDelivery(claim, result);
            return ValueTask.CompletedTask;
        }

        return (_innerClaimStore ?? throw new InvalidOperationException(
            "The configured post-commit outbox store does not provide atomic claim support."))
            .RecordDeliveryResultAsync(claim, result, cancellationToken);
    }

    public async ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> CompleteClaimAsync(
        RuntimePostCommitOutboxClaimCompletion completion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);

        if (sessionAccessor.Current is { } session && session.IsActive && OwnsOverlayContinuation(session, completion.Claim.OutboxItemId))
        {
            var childExecution = completion.WorkflowDispatch is { } dispatch
                ? await workflowExecutionStateStore.FindAsync(dispatch.ChildWorkflowExecutionId, cancellationToken)
                : null;
            return session.CompleteOutboxClaim(completion, childExecution);
        }

        return await (_innerCompletionStore ?? throw new InvalidOperationException(
            "The configured post-commit outbox store does not provide atomic claim completion support."))
            .CompleteClaimAsync(completion, cancellationToken);
    }
}
