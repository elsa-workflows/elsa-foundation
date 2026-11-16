using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Forwards every outbox contract the processor and the drain orchestrator use to one inner store, so a test double
/// overrides only the calls it observes. The inner store must provide each capability a test reaches.
/// </summary>
internal class ForwardingOutboxStore(IRuntimePostCommitOutboxStore inner) :
    IRuntimePostCommitOutboxStore,
    IRuntimePostCommitOutboxClaimStore,
    IRuntimePostCommitOutboxClaimCompletionStore,
    IPostCommitOutboxLookupStore
{
    private IRuntimePostCommitOutboxClaimStore Claims => (IRuntimePostCommitOutboxClaimStore)inner;

    public virtual ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetDeliverableAsync(
        RuntimePostCommitOutboxQuery query,
        CancellationToken cancellationToken = default) =>
        inner.GetDeliverableAsync(query, cancellationToken);

    public ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> RecordDeliveryResultAsync(
        RuntimePostCommitOutboxDeliveryResult result,
        CancellationToken cancellationToken = default) =>
        inner.RecordDeliveryResultAsync(result, cancellationToken);

    public virtual ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxClaim>> ClaimAsync(
        RuntimePostCommitOutboxClaimRequest request,
        CancellationToken cancellationToken = default) =>
        Claims.ClaimAsync(request, cancellationToken);

    public ValueTask<RuntimePostCommitOutboxClaim?> RenewClaimAsync(
        RuntimePostCommitOutboxClaim claim,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default) =>
        Claims.RenewClaimAsync(claim, now, visibilityTimeout, cancellationToken);

    public ValueTask RecordDeliveryResultAsync(
        RuntimePostCommitOutboxClaim claim,
        RuntimePostCommitOutboxDeliveryResult result,
        CancellationToken cancellationToken = default) =>
        Claims.RecordDeliveryResultAsync(claim, result, cancellationToken);

    public virtual ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> ListClaimedAsync(
        RuntimePostCommitOutboxClaimedQuery query,
        CancellationToken cancellationToken = default) =>
        Claims.ListClaimedAsync(query, cancellationToken);

    public ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> CompleteClaimAsync(
        RuntimePostCommitOutboxClaimCompletion completion,
        CancellationToken cancellationToken = default) =>
        ((IRuntimePostCommitOutboxClaimCompletionStore)inner).CompleteClaimAsync(completion, cancellationToken);

    public ValueTask<RuntimePostCommitOutboxItem?> FindAsync(
        string outboxItemId,
        CancellationToken cancellationToken = default) =>
        ((IPostCommitOutboxLookupStore)inner).FindAsync(outboxItemId, cancellationToken);
}
