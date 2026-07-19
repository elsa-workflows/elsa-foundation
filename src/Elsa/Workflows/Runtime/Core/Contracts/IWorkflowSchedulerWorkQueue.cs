using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Stores scheduler work recorded by execution agents, isolated by workflow execution ID.
/// </summary>
public interface IWorkflowSchedulerWorkQueue
{
    /// <summary>
    /// Indicates whether this provider supplies provider-atomic claim, renewal, release, and completion
    /// transitions. Legacy providers remain usable through the original single-writer dequeue contract.
    /// </summary>
    bool SupportsClaimTransitions => false;

    ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyCollection<RuntimeSchedulerWorkItem>> ListAsync(RuntimeSchedulerWorkQuery query, CancellationToken cancellationToken = default);
    ValueTask<RuntimeSchedulerWorkItem?> DequeueAsync(string workflowExecutionId, CancellationToken cancellationToken = default);
    ValueTask<bool> DeleteAsync(string workflowExecutionId, string workItemId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This scheduler work queue does not support targeted deletion.");

    /// <summary>
    /// Lists the distinct workflow execution IDs that currently have pending scheduler work, ordered
    /// deterministically (ordinal). Used by system-wide resumption sweeps to discover executions whose
    /// queued work survived a process restart and would otherwise never be drained.
    /// </summary>
    ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically claims the FIFO head when it is visible. An unexpired claim keeps the head hidden and
    /// prevents later work in the same workflow execution from overtaking it.
    /// </summary>
    ValueTask<RuntimeSchedulerWorkClaim?> ClaimAsync(
        RuntimeSchedulerWorkClaimRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This scheduler work queue does not support claim transitions.");

    /// <summary>Renews a claim only while its owner, fencing token, and provider revision are current.</summary>
    ValueTask<RuntimeSchedulerWorkClaimTransitionResult> RenewClaimAsync(
        RuntimeSchedulerWorkClaim claim,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This scheduler work queue does not support claim transitions.");

    /// <summary>Permanently removes work only while the presented claim is current.</summary>
    ValueTask<RuntimeSchedulerWorkClaimTransitionResult> CompleteClaimAsync(
        RuntimeSchedulerWorkClaim claim,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This scheduler work queue does not support claim transitions.");

    /// <summary>Releases a current claim without removing its work item.</summary>
    ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ReleaseClaimAsync(
        RuntimeSchedulerWorkClaim claim,
        DateTimeOffset visibleAt,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This scheduler work queue does not support claim transitions.");
}
