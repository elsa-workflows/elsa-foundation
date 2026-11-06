using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Single-provider clock and timer ownership, separate from the ordinary durable timer store. Providers must
/// atomically fence create/schedule/advance/claim against the same normal test-scope closure/expiry boundary;
/// an application-side AssertOpen followed by a separate write is insufficient. No default durable provider is implied.
/// </summary>
public interface IControlledWorkflowTimeStore
{
    /// <summary>Explicit idempotent preparation. Existing identity/material must match; never recreate a closed clock.</summary>
    ValueTask<ControlledWorkflowClockState> CreateClockAsync(WorkflowTestTimePolicy policy,
        DateTimeOffset observedAt, CancellationToken cancellationToken = default);

    ValueTask<ControlledWorkflowClockState?> FindClockAsync(WorkflowTestTimePolicy policy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Existing exact timer wins. Validate its policy/invocation and the stamped clock read against retained clock
    /// revision evidence, preserving a legitimate read made just before advancement. Save always retains scan
    /// responsibility, including a timer saved after an advance made its deadline already due.
    /// </summary>
    ValueTask<DurableTimer> ScheduleAsync(WorkflowTestTimePolicy policy, DurableTimer timer,
        DateTimeOffset observedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Advance monotonically with CAS and scoped operation/material idempotency. Retain pending due scans until
    /// all eligible timers settle; advancing does not change wall lease/visibility/expiry values.
    /// </summary>
    ValueTask<ControlledWorkflowClockState> AdvanceAsync(WorkflowTestTimePolicy policy, string operationId,
        long expectedRevision, DateTimeOffset advanceTo, DateTimeOffset observedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bounded pending/retry/expiry work. Both timer save and clock advance add responsibility. Returning a page
    /// cannot consume it; only settlement/closure does. The provider applies wall retry visibility before its limit.
    /// </summary>
    ValueTask<IReadOnlyList<WorkflowTestTimePolicy>> ListPendingAsync(DateTimeOffset observedAt, int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Select exact-policy due timers against the atomically read scenario clock, while claimed-at, visibility,
    /// fences and retry eligibility use observedAt. Scenario time must never become an infrastructure lease instant.
    /// </summary>
    ValueTask<IReadOnlyList<ControlledWorkflowTimerClaim>> ClaimDueAsync(WorkflowTestTimePolicy policy,
        string ownerId, DateTimeOffset observedAt, TimeSpan visibility, int limit,
        CancellationToken cancellationToken = default);

    ValueTask<ControlledWorkflowTimerTransition> RenewClaimAsync(ControlledWorkflowTimerClaim claim,
        DateTimeOffset observedAt, TimeSpan visibility, CancellationToken cancellationToken = default);

    /// <summary>Persist exact normal bookmark receipt before dispatch; return the updated fenced claim.</summary>
    ValueTask<ControlledWorkflowTimerTransition> BindDeliveryAsync(ControlledWorkflowTimerClaim claim,
        TestRunDeliveryReceipt delivery, CancellationToken cancellationToken = default);

    ValueTask<ControlledWorkflowTimerTransition> CompleteClaimAsync(ControlledWorkflowTimerClaim claim,
        CancellationToken cancellationToken = default);

    ValueTask<ControlledWorkflowTimerTransition> ReleaseClaimAsync(ControlledWorkflowTimerClaim claim,
        DateTimeOffset visibleAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bounded idempotent owned-timer cleanup after the normal scope closes/expires. Fence late scheduling/claims;
    /// do not cancel or delete unrelated execution resources. Remaining work stays discoverable by ListPendingAsync.
    /// </summary>
    ValueTask<ControlledWorkflowTimeCleanupResult> CloseAsync(WorkflowTestTimePolicy policy,
        DateTimeOffset observedAt, int limit, CancellationToken cancellationToken = default);
}
