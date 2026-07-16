using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services.Coalescing;

/// <summary>
/// Coalescing-aware overlay for <see cref="IRuntimePostCommitOutboxStore"/>. While a coalescing session owns the target
/// workflow execution, deliverable continuation intents are read from the session's overlay outbox and delivery results
/// recorded there, so intra-segment continuation is delivered from the working set and not durably persisted until the
/// flush. When no session is active it is a byte-for-byte pass-through to the durable inner outbox store.
/// </summary>
public sealed class CoalescingRuntimePostCommitOutboxStore(
    CoalescingInner<IRuntimePostCommitOutboxStore> inner,
    IRuntimeCoalescingSessionAccessor sessionAccessor) : IRuntimePostCommitOutboxStore, IRuntimePostCommitOutboxClaimStore, IRuntimePostCommitOutboxClaimCompletionStore
{
    private readonly IRuntimePostCommitOutboxStore _inner = inner.Value;
    private readonly IRuntimePostCommitOutboxClaimStore? _innerClaimStore = inner.Value as IRuntimePostCommitOutboxClaimStore;
    private readonly IRuntimePostCommitOutboxClaimCompletionStore? _innerCompletionStore = inner.Value as IRuntimePostCommitOutboxClaimCompletionStore;

    public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetDeliverableAsync(RuntimePostCommitOutboxQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.WorkflowExecutionId is { } workflowExecutionId &&
            sessionAccessor.Current is { } session && session.AppliesTo(workflowExecutionId))
            return new ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>>(session.GetDeliverableOutbox(query));

        return _inner.GetDeliverableAsync(query, cancellationToken);
    }

    public ValueTask RecordDeliveryResultAsync(RuntimePostCommitOutboxDeliveryResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (sessionAccessor.Current is { } session && session.IsActive && session.OwnsOutboxItem(result.OutboxItemId))
        {
            session.RecordOutboxDelivery(result);
            return ValueTask.CompletedTask;
        }

        return _inner.RecordDeliveryResultAsync(result, cancellationToken);
    }

    public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxClaim>> ClaimAsync(
        RuntimePostCommitOutboxClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.WorkflowExecutionId is { } workflowExecutionId &&
            sessionAccessor.Current is { } session && session.AppliesTo(workflowExecutionId))
            return new ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxClaim>>(session.ClaimOutbox(request));

        return (_innerClaimStore ?? throw new InvalidOperationException(
            "The configured post-commit outbox store does not provide atomic claim support."))
            .ClaimAsync(request, cancellationToken);
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

    public ValueTask CompleteClaimAsync(
        RuntimePostCommitOutboxClaimCompletion completion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);

        if (sessionAccessor.Current is { } session && session.IsActive && session.OwnsOutboxItem(completion.Claim.OutboxItemId))
        {
            session.CompleteOutboxClaim(completion);
            return ValueTask.CompletedTask;
        }

        return (_innerCompletionStore ?? throw new InvalidOperationException(
            "The configured post-commit outbox store does not provide atomic claim completion support."))
            .CompleteClaimAsync(completion, cancellationToken);
    }
}
