using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;

namespace Elsa.Workflows.Runtime.Core.Contracts.Alterations;

/// <summary>
/// Durable plan and target/job state for runtime alterations. Implementations make captured targets claimable only
/// after sealing and apply terminal actor evidence in the workflow checkpoint's storage unit.
/// </summary>
public interface IWorkflowAlterationStore
{
    ValueTask<WorkflowAlterationPlanAdmissionResult> AdmitAsync(WorkflowAlterationPlanState plan, CancellationToken cancellationToken = default);
    ValueTask<WorkflowAlterationPlanState?> FindPlanAsync(string planId, CancellationToken cancellationToken = default);
    /// <summary>Lists a bounded page of non-terminal plans in stable least-recently-serviced order for restart-safe orchestration.</summary>
    ValueTask<WorkflowAlterationActivePlanPage> ListActivePlansAsync(int pageSize, string? cursor = null, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new WorkflowAlterationActivePlanPage([], null, false));
    /// <summary>Moves a still-active plan to the tail of the durable orchestration order.</summary>
    ValueTask RescheduleActivePlanAsync(string planId, DateTimeOffset servicedAt, CancellationToken cancellationToken = default);
    ValueTask<WorkflowAlterationPlanState> CaptureAsync(string planId, long expectedRevision, IReadOnlyCollection<WorkflowAlterationCapturedTarget> targets, string? nextCursor, CancellationToken cancellationToken = default);
    ValueTask<WorkflowAlterationPlanState> SealAsync(string planId, long expectedRevision, DateTimeOffset sealedAt, CancellationToken cancellationToken = default);
    /// <summary>Deletes one bounded page of provisional jobs and returns Cancelling until the unsealed capture can atomically become Cancelled.</summary>
    ValueTask<WorkflowAlterationPlanState> CancelUnsealedCaptureAsync(string planId, DateTimeOffset cancelledAt, CancellationToken cancellationToken = default);
    /// <summary>Deletes one bounded page of provisional jobs and returns Cancelling until the unsealed capture can atomically become Failed.</summary>
    ValueTask<WorkflowAlterationPlanState> FailUnsealedCaptureAsync(string planId, WorkflowAlterationSafeFailure safeFailure, DateTimeOffset failedAt, CancellationToken cancellationToken = default);
    ValueTask<WorkflowAlterationPlanState> RequestCancellationAsync(string planId, DateTimeOffset requestedAt, CancellationToken cancellationToken = default);
    /// <summary>Marks at most <paramref name="maximumCount"/> not-yet-claimed jobs cancelled using one caller-derived safe skipped outcome per envelope.</summary>
    ValueTask CancelPendingJobsAsync(string planId, IReadOnlyCollection<WorkflowAlterationOutcome> skippedOutcomes, DateTimeOffset completedAt, int maximumCount, CancellationToken cancellationToken = default);
    ValueTask<WorkflowAlterationJobState?> FindJobAsync(string jobId, CancellationToken cancellationToken = default);
    /// <summary>Returns durable status counts without paging every job in the plan.</summary>
    ValueTask<WorkflowAlterationJobCounts> GetJobCountsAsync(string planId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new WorkflowAlterationJobCounts(0, 0, 0, 0, 0));
    /// <summary>Locates terminal evidence by its deterministic checkpoint identity for acknowledgement reconciliation.</summary>
    ValueTask<WorkflowAlterationJobState?> FindJobByCheckpointCommitIdAsync(string checkpointCommitId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<WorkflowAlterationJobState?>(null);
    ValueTask<WorkflowAlterationJobPage> PageJobsAsync(string planId, int pageSize, string? cursor = null, CancellationToken cancellationToken = default);
    /// <summary>Claims the next leaseable job belonging to one sealed plan.</summary>
    ValueTask<WorkflowAlterationJobState?> ClaimNextAsync(string planId, string ownerId, DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken = default);
    ValueTask<WorkflowAlterationPlanState> ReconcileAsync(string planId, DateTimeOffset now, CancellationToken cancellationToken = default);
    ValueTask ValidateTerminalJobChangeAsync(WorkflowAlterationJobTerminalChange change, CancellationToken cancellationToken = default);
    ValueTask ApplyTerminalJobChangeAsync(WorkflowAlterationJobTerminalChange change, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the workflow checkpoint write (which carries the terminal-job transition) as one provider transaction.
    /// The default keeps legacy providers source-compatible; durable providers must override it instead of composing
    /// separate calls.
    /// </summary>
    async ValueTask CommitTerminalJobChangeAtomicallyAsync(
        WorkflowAlterationJobTerminalChange change,
        Func<CancellationToken, ValueTask> commitWorkflowCheckpointAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(commitWorkflowCheckpointAsync);
        await ValidateTerminalJobChangeAsync(change, cancellationToken);
        await commitWorkflowCheckpointAsync(cancellationToken);
    }
}
