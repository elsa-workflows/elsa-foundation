using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;

namespace Elsa.Workflows.Runtime.Services.Coalescing;

/// <summary>
/// Coalescing-aware overlay for <see cref="IRuntimePostCommitOutboxStore"/>. While a coalescing session owns the target
/// workflow execution, deliverable continuation intents are read from the session's overlay outbox and delivery results
/// recorded there, so intra-segment continuation is delivered from the working set and not durably persisted until the
/// flush. When no session is active it is a byte-for-byte pass-through to the durable inner outbox store.
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

        if (query.WorkflowExecutionId is { } workflowExecutionId &&
            sessionAccessor.Current is { } session && session.AppliesTo(workflowExecutionId))
            return new ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>>(session.GetDeliverableOutbox(query));

        return _inner.GetDeliverableAsync(query, cancellationToken);
    }

    public ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> RecordDeliveryResultAsync(RuntimePostCommitOutboxDeliveryResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (sessionAccessor.Current is { } session && session.IsActive && session.OwnsOutboxItem(result.OutboxItemId))
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

    public ValueTask<RuntimePostCommitOutboxItem?> FindUndeliveredDurableAsync(string workflowExecutionId, string excludedIntentKind,
        CancellationToken cancellationToken = default) =>
        (_innerLookupStore ?? throw new NotSupportedException("The inner provider cannot verify prior durable outbox responsibility."))
            .FindUndeliveredDurableAsync(workflowExecutionId, excludedIntentKind, cancellationToken);

    public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxClaim>> ClaimAsync(
        RuntimePostCommitOutboxClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.WorkflowExecutionId is { } workflowExecutionId &&
            sessionAccessor.Current is { } session && session.AppliesTo(workflowExecutionId))
            return new ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxClaim>>(session.ClaimOutbox(request, item => item.StimulusDeliverySourceScope is null && !requiredSourceKinds.Contains(item.Intent.Kind)));

        return (_innerClaimStore ?? throw new InvalidOperationException(
            "The configured post-commit outbox store does not provide atomic claim support."))
            .ClaimAsync(request, cancellationToken);
    }

    public ValueTask<RuntimePostCommitOutboxClaim?> RenewClaimAsync(
        RuntimePostCommitOutboxClaim claim,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);

        if (sessionAccessor.Current is { } session && session.IsActive && session.OwnsOutboxItem(claim.OutboxItemId))
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

        if (sessionAccessor.Current is { } session && session.IsActive && session.OwnsOutboxItem(claim.OutboxItemId))
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

        if (sessionAccessor.Current is { } session && session.IsActive && session.OwnsOutboxItem(completion.Claim.OutboxItemId))
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
