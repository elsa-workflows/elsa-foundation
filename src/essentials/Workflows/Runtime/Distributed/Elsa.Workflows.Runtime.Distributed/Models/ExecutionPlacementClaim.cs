using Elsa.Workflows.Runtime.Distributed.Contracts;

namespace Elsa.Workflows.Runtime.Distributed.Models;

/// <summary>
/// A request to claim (or renew) placement of a workflow execution for a node. The service computes
/// <see cref="ExpiresAt"/> from <see cref="ExecutionPlacementOptions.LeaseDuration"/>; the store applies the claim
/// atomically against the current lease.
/// </summary>
public sealed class ExecutionPlacementClaim
{
    public ExecutionPlacementClaim(
        string workflowExecutionId,
        string ownerId,
        DateTimeOffset requestedAt,
        DateTimeOffset expiresAt)
    {
        DistributedRuntimeIdentityConstraints.Validate(workflowExecutionId, nameof(workflowExecutionId));
        DistributedRuntimeIdentityConstraints.Validate(ownerId, nameof(ownerId));

        if (expiresAt <= requestedAt)
            throw new ArgumentException("Placement claim expiration must be later than the request time.", nameof(expiresAt));

        WorkflowExecutionId = workflowExecutionId;
        OwnerId = ownerId;
        RequestedAt = requestedAt;
        ExpiresAt = expiresAt;
    }

    public string WorkflowExecutionId { get; }
    public string OwnerId { get; }
    public DateTimeOffset RequestedAt { get; }
    public DateTimeOffset ExpiresAt { get; }
}

public enum ExecutionPlacementClaimOutcome
{
    /// <summary>The claim took placement that was unowned or held by an expired lease from another node.</summary>
    Granted,

    /// <summary>The claim renewed placement the claiming node already held.</summary>
    Renewed,

    /// <summary>Placement is held by a live lease belonging to another node; the claim was refused.</summary>
    Denied
}

/// <summary>
/// The outcome of a <see cref="Contracts.IExecutionPlacementStore.TryClaimAsync"/> attempt. On
/// <see cref="ExecutionPlacementClaimOutcome.Granted"/> / <see cref="ExecutionPlacementClaimOutcome.Renewed"/>,
/// <see cref="Lease"/> is the newly issued lease the caller now holds; on
/// <see cref="ExecutionPlacementClaimOutcome.Denied"/>, <see cref="Lease"/> is the live lease of the current owner.
/// </summary>
public sealed class ExecutionPlacementClaimResult
{
    public ExecutionPlacementClaimResult(ExecutionPlacementClaimOutcome outcome, ExecutionPlacementLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        Outcome = outcome;
        Lease = lease;
    }

    public ExecutionPlacementClaimOutcome Outcome { get; }
    public ExecutionPlacementLease Lease { get; }

    public bool IsOwnedByClaimant => Outcome is ExecutionPlacementClaimOutcome.Granted or ExecutionPlacementClaimOutcome.Renewed;
}
