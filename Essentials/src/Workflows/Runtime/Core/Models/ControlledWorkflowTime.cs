namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Mutable clock state belongs to its immutable admitted policy, never the reverse.</summary>
public sealed record ControlledWorkflowClockState(
    WorkflowTestTimePolicy Policy, DateTimeOffset CurrentTime, long Revision, bool Closed = false)
{
    public WorkflowBusinessTime Read() => new(CurrentTime, Policy.Entry.ContextId,
        Policy.Entry.ContextFingerprint, Policy.ClockId, Revision);
}

/// <summary>A timer claim whose due eligibility is scenario-owned and whose lease is wall-owned.</summary>
public sealed record ControlledWorkflowTimerClaim(
    WorkflowTestTimePolicy Policy, RuntimeDurableTimerClaim Claim, TestRunDeliveryReceipt? Delivery = null);

public sealed record ControlledWorkflowTimerTransition(
    RuntimeDurableTimerClaimTransitionStatus Status, ControlledWorkflowTimerClaim? Claim = null)
{
    public bool Succeeded => Status is RuntimeDurableTimerClaimTransitionStatus.Succeeded or
        RuntimeDurableTimerClaimTransitionStatus.AlreadyApplied;
}

/// <summary>Bounded delivery progress; pending responsibility is not successful settlement.</summary>
public sealed record ControlledWorkflowTimerDrainResult(int Claimed, int Dispatched, int Pending, int Retired);

public sealed record ControlledWorkflowTimeCleanupResult(int Removed, int Remaining);
