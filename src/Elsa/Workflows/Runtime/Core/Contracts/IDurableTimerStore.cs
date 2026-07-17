using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Durable store for <see cref="DurableTimer"/> documents. The default in-memory implementation keeps
/// timers only for the process lifetime; a durable persistence provider (e.g. the Groundwork bridge)
/// swaps in a restart-surviving implementation so a <c>Delay</c> survives a process restart.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SaveAsync"/> is an idempotent upsert keyed by (<see cref="DurableTimer.WorkflowExecutionId"/>,
/// <see cref="DurableTimer.TimerId"/>): an existing timer wins and is returned, so a crash-replay re-invoke
/// of the same activity execution — which derives the same deterministic timer id — cannot create a
/// duplicate or shift an already-committed deadline.
/// </para>
/// <para>
/// <see cref="ListDueAsync"/> returns timers whose <see cref="DurableTimer.DueTime"/> is at or before the
/// supplied instant, ordered by (DueTime, TimerId) and capped by <paramref name="limit"/>. The limit bounds
/// the number of timers the pump dispatches per tick, not the underlying scan.
/// </para>
/// </remarks>
public interface IDurableTimerStore
{
    /// <summary>
    /// Indicates whether this provider supplies provider-atomic due claims, renewal, release, and completion.
    /// Legacy providers remain usable through the original list/delete contract.
    /// </summary>
    bool SupportsClaimTransitions => false;

    /// <summary>Upserts a timer (existing wins) and returns the stored timer.</summary>
    ValueTask<DurableTimer> SaveAsync(DurableTimer timer, CancellationToken cancellationToken = default);

    /// <summary>Returns due timers (DueTime &lt;= <paramref name="asOf"/>), ordered by due time then id, capped by <paramref name="limit"/>.</summary>
    ValueTask<IReadOnlyCollection<DurableTimer>> ListDueAsync(DateTimeOffset asOf, int limit, CancellationToken cancellationToken = default);

    /// <summary>Finds a single timer by its identity, or <c>null</c> if it does not exist.</summary>
    ValueTask<DurableTimer?> FindAsync(string workflowExecutionId, string timerId, CancellationToken cancellationToken = default);

    /// <summary>Lists every timer owned by one workflow execution for provider-neutral scope cleanup.</summary>
    ValueTask<IReadOnlyCollection<DurableTimer>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This durable timer store does not support workflow-scoped listing.");

    /// <summary>Deletes a timer by its identity. Deleting a missing timer is a no-op.</summary>
    ValueTask DeleteAsync(string workflowExecutionId, string timerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically claims at most the requested number of visible due timers. Competing claimants cannot
    /// receive the same current fencing token.
    /// </summary>
    ValueTask<IReadOnlyCollection<RuntimeDurableTimerClaim>> ClaimDueAsync(
        RuntimeDurableTimerClaimRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This durable timer store does not support claim transitions.");

    /// <summary>Renews a timer claim only while its owner, fencing token, and provider revision are current.</summary>
    ValueTask<RuntimeDurableTimerClaimTransitionResult> RenewClaimAsync(
        RuntimeDurableTimerClaim claim,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This durable timer store does not support claim transitions.");

    /// <summary>Permanently removes a timer only while the presented claim is current.</summary>
    ValueTask<RuntimeDurableTimerClaimTransitionResult> CompleteClaimAsync(
        RuntimeDurableTimerClaim claim,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This durable timer store does not support claim transitions.");

    /// <summary>
    /// Releases a current claim without removing its timer. The timer remains hidden until
    /// <paramref name="visibleAt"/>.
    /// </summary>
    ValueTask<RuntimeDurableTimerClaimTransitionResult> ReleaseClaimAsync(
        RuntimeDurableTimerClaim claim,
        DateTimeOffset visibleAt,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This durable timer store does not support claim transitions.");
}
