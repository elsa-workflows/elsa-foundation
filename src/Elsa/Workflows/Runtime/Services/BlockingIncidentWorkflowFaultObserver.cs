using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Consumes recorded incidents to drive the workflow-level fault policy (RT-1 gap a, RT-5). After a drain, if the
/// workflow has one or more blocking incidents and has not already reached a terminal status, this observer commits
/// a <see cref="RuntimeCheckpointNames.WorkflowFaulted"/> checkpoint transitioning the workflow execution status to
/// <see cref="WorkflowExecutionStatus.Faulted"/> — making a faulted workflow observable instead of stuck in
/// <see cref="WorkflowExecutionStatus.Running"/> forever.
///
/// <para><b>Faulted semantics.</b> <see cref="WorkflowExecutionStatus.Faulted"/> is a terminal status: once set, the
/// drainer's terminal-status gate stops further scheduler work. Recovering a faulted workflow (resolving the blocking
/// incident and resuming) is a separate operator/intervention surface, not part of this transition.</para>
/// </summary>
public sealed class BlockingIncidentWorkflowFaultObserver : IWorkflowSchedulerDrainObserver
{
    private const string FaultReason = "BlockingIncident";
    private readonly IIncidentStateStore _incidentStateStore;
    private readonly IWorkflowExecutionStateStore _workflowExecutionStateStore;
    private readonly RuntimeCheckpointCommitter _checkpointCommitter;
    private readonly TimeProvider _timeProvider;

    public BlockingIncidentWorkflowFaultObserver(
        IIncidentStateStore incidentStateStore,
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        RuntimeCheckpointCommitter checkpointCommitter,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(incidentStateStore);
        ArgumentNullException.ThrowIfNull(workflowExecutionStateStore);
        ArgumentNullException.ThrowIfNull(checkpointCommitter);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _incidentStateStore = incidentStateStore;
        _workflowExecutionStateStore = workflowExecutionStateStore;
        _checkpointCommitter = checkpointCommitter;
        _timeProvider = timeProvider;
    }

    public async ValueTask OnDrainedAsync(
        WorkflowExecutionCommandEnvelope envelope,
        RuntimeSchedulerDrainResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(result);

        var workflowExecutionId = envelope.WorkflowExecutionId;

        var workflowState = await _workflowExecutionStateStore.FindAsync(workflowExecutionId, cancellationToken);
        if (workflowState is null || workflowState.Status.IsTerminal())
            return;

        var blockingIncidents = await _incidentStateStore.ListBlockingAsync(workflowExecutionId, cancellationToken);
        // A missing stable consumer/schema is a deployment compatibility incident. Keep the run recoverable
        // while it waits for deployment correction; ordinary activity faults still terminalize the workflow.
        if (blockingIncidents.All(x => StringComparer.Ordinal.Equals(
                x.FailureType,
                ActivityActivationFailureHandler.IncidentFailureType)))
            return;

        var occurredAt = _timeProvider.GetUtcNow();
        var faultedState = workflowState with
        {
            Status = WorkflowExecutionStatus.Faulted,
            UpdatedAt = occurredAt,
            CompletedAt = occurredAt
        };

        var checkpointId = $"checkpoint:{workflowExecutionId}:workflow-faulted";
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RuntimeMetadataKeys.CheckpointReason] = FaultReason
        };

        var commit = new RuntimeCheckpointCommit(
            CommitId: $"commit:{workflowExecutionId}:workflow-faulted",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: checkpointId,
                Name: RuntimeCheckpointNames.WorkflowFaulted,
                WorkflowExecutionId: workflowExecutionId,
                OccurredAt: occurredAt,
                ActivityExecutionIds: [],
                Metadata: metadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: new RuntimeStateChange<WorkflowExecutionState>(
                    StateId: workflowExecutionId,
                    Operation: RuntimeStateChangeOperation.Upsert,
                    State: faultedState,
                    Metadata: metadata),
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: []),
            PostCommitIntents: [],
            Metadata: metadata);

        await _checkpointCommitter.CommitAsync(commit, cancellationToken);
    }
}
