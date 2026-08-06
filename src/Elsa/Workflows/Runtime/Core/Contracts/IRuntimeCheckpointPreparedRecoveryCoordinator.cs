using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Verifies and recovers the durable Prepared frontier before a coalesced drain may dispatch scheduler work.
/// Implementations require the ambient active ownership lease to match the requested workflow execution, use that
/// lease's exact <see cref="RuntimeExecutionLease.ToFence"/> value as the recovery target, and fail closed before
/// paging or mutation when ownership is absent or mismatched. They also fail closed when the frontier cannot be
/// routed or adopted exactly.
/// </summary>
public interface IRuntimeCheckpointPreparedRecoveryCoordinator
{
    /// <summary>
    /// Recovers the exact durable Prepared frontier under the matching ambient active ownership lease, or throws
    /// before paging, mutation, or source dispatch when that ownership precondition is not satisfied.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="workflowExecutionId"/> is blank.</exception>
    /// <exception cref="Elsa.Workflows.Runtime.Core.Exceptions.RuntimeCheckpointPreparedRecoveryException">
    /// The durable frontier is unsafe, incomplete, ambiguous, stale, or cannot be atomically finalized.
    /// </exception>
    ValueTask<RuntimeCheckpointPreparedRecoveryResult> RecoverPreparedAsync(
        string workflowExecutionId,
        CancellationToken cancellationToken = default);
}
