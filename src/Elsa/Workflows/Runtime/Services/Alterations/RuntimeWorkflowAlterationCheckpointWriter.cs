using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>
/// Bridges generic alteration preflight to the Runtime checkpoint protocol. The terminal job record travels inside the
/// same state-change set as the staged workflow state, so a worker never reports success before durable evidence exists.
/// </summary>
public sealed class RuntimeWorkflowAlterationCheckpointWriter(
    RuntimeCheckpointCommitter checkpointCommitter,
    IWorkflowAlterationStore alterationStore,
    TimeProvider timeProvider) : IWorkflowAlterationCheckpointWriter
{
    private readonly RuntimeCheckpointCommitter _checkpointCommitter = checkpointCommitter ?? throw new ArgumentNullException(nameof(checkpointCommitter));
    private readonly IWorkflowAlterationStore _alterationStore = alterationStore ?? throw new ArgumentNullException(nameof(alterationStore));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async ValueTask<WorkflowAlterationCheckpointWriteResult> CommitAsync(
        WorkflowAlterationCheckpointWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Job);
        ArgumentNullException.ThrowIfNull(request.TerminalJobChange);
        ArgumentNullException.ThrowIfNull(request.ProjectedState);

        var workflow = request.ProjectedState.GetRequired<WorkflowExecutionState>();
        if (!StringComparer.Ordinal.Equals(workflow.WorkflowExecutionId, request.Job.WorkflowExecutionId))
            throw new InvalidOperationException("The alteration projected workflow state does not belong to the claimed target job.");

        var builder = new WorkflowAlterationRuntimeCheckpointStateBuilder(workflow);
        foreach (var stagedChange in request.StagedChanges)
        {
            if (stagedChange is not IWorkflowAlterationRuntimeCheckpointStagedChange checkpointStagedChange)
            {
                throw new InvalidOperationException(
                    $"Alteration staged change '{stagedChange.ChangeKey}' does not implement {nameof(IWorkflowAlterationRuntimeCheckpointStagedChange)} and cannot enter a runtime checkpoint.");
            }

            checkpointStagedChange.Apply(builder);
        }

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RuntimeMetadataKeys.CheckpointReason] = RuntimeCheckpointNames.RuntimeAlterationJob,
            [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory,
            ["runtime.alteration.jobId"] = request.Job.JobId,
            ["runtime.alteration.planId"] = request.Job.PlanId
        };
        var occurredAt = _timeProvider.GetUtcNow();
        var commit = new RuntimeCheckpointCommit(
            request.TerminalJobChange.CheckpointCommitId,
            new RuntimeCheckpoint(
                $"checkpoint:{request.TerminalJobChange.CheckpointCommitId}",
                RuntimeCheckpointNames.RuntimeAlterationJob,
                request.Job.WorkflowExecutionId,
                occurredAt,
                [],
                metadata),
            new RuntimeCheckpointStateChangeSet(
                new RuntimeStateChange<WorkflowExecutionState>(workflow.WorkflowExecutionId, RuntimeStateChangeOperation.Upsert, builder.WorkflowExecution, metadata),
                null,
                builder.ActivityExecutions,
                [],
                [],
                [],
                [],
                null,
                builder.ActivityExecutionInspections,
                null,
                builder.ActivityScopeCleanups,
                builder.WorkflowDispatchCancellations,
                null,
                request.TerminalJobChange),
            builder.PostCommitIntents.ToArray(),
            metadata);

        await _alterationStore.CommitTerminalJobChangeAtomicallyAsync(
            request.TerminalJobChange,
            cancellationToken => CommitCheckpointAsync(commit, cancellationToken),
            cancellationToken);
        return new WorkflowAlterationCheckpointWriteResult(request.TerminalJobChange.CheckpointCommitId, request.TerminalJobChange);
    }

    public async ValueTask<WorkflowAlterationCheckpointWriteResult?> FindCommittedAsync(
        string checkpointCommitId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointCommitId);
        var job = await _alterationStore.FindJobByCheckpointCommitIdAsync(checkpointCommitId, cancellationToken);
        if (job is null || job.Claim is null || job.CheckpointCommitId is null || job.Status is not (WorkflowAlterationJobStatus.Succeeded or WorkflowAlterationJobStatus.Failed or WorkflowAlterationJobStatus.Cancelled))
            return null;

        return new WorkflowAlterationCheckpointWriteResult(
            checkpointCommitId,
            new WorkflowAlterationJobTerminalChange(
                job.JobId,
                job.Claim.Token,
                job.Status,
                job.Outcomes,
                checkpointCommitId,
                job.CompletedAt ?? _timeProvider.GetUtcNow(),
                job.SafeFailure));
    }

    private async ValueTask CommitCheckpointAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken)
    {
        await _checkpointCommitter.CommitAsync(commit, cancellationToken);
    }
}
