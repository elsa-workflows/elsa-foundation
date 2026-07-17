using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Establishes and enforces single-writer ownership of a workflow execution (RT-2).
/// </summary>
/// <remarks>
/// Ownership is expressed as a monotonically increasing fencing token. Each acquisition issues a strictly greater
/// token than any previously issued for the same workflow execution — tokens are never reused, even across a clean
/// release — so a superseded writer holding an older token can always be distinguished from the current owner. The
/// checkpoint store receives the current fence and validates it as part of its atomic durable commit decision.
/// </remarks>
public interface IRuntimeExecutionOwnershipService
{
    /// <summary>
    /// Acquires (or renews ownership of) the workflow execution, issuing a strictly greater fencing token than any
    /// previously issued for it. Persists an execution lease and heartbeat to operational state so the recovery
    /// scanner can observe the owner while the writer is active — and so an interrupted writer that never released
    /// remains detectable.
    /// </summary>
    ValueTask<RuntimeExecutionLease> AcquireAsync(string workflowExecutionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes the heartbeat (and lease expiry) for an active lease so a long-running drain is not mistaken for an
    /// interrupted execution.
    /// </summary>
    ValueTask<RuntimeExecutionOwnershipTransitionResult> HeartbeatAsync(
        RuntimeExecutionLease lease,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases the lease on clean completion by clearing the operational lease and heartbeat while preserving the
    /// issued fencing token counter, so a subsequent acquisition never reuses a token. A writer that crashes never
    /// reaches this call, leaving the lease in place for the recovery scanner to detect.
    /// </summary>
    ValueTask<RuntimeExecutionOwnershipTransitionResult> ReleaseAsync(
        RuntimeExecutionLease lease,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Throws <see cref="Exceptions.RuntimeStaleFencingTokenException"/> when <paramref name="fencingToken"/> is not the
    /// current active owner's token for the workflow execution. Released and expired leases are rejected even when
    /// they carry the highest token ever issued. This remains a convenience preflight; durable checkpoint authority
    /// belongs to the checkpoint store's atomic fence decision. A no-op when ownership was never established.
    /// </summary>
    ValueTask EnsureCurrentAsync(string workflowExecutionId, long fencingToken, CancellationToken cancellationToken = default);
}
