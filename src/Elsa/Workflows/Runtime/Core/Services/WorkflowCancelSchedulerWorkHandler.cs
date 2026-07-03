using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class WorkflowCancelSchedulerWorkHandler : IWorkflowSchedulerWorkHandler, IRuntimePipelineWorkHandler
{
    public const string HandlerName = nameof(WorkflowCancelSchedulerWorkHandler);
    private const string CancellationReason = "WorkflowCancellation";
    private readonly IWorkflowExecutionStateStore _workflowExecutionStateStore;
    private readonly IActivityExecutionStateStore _activityExecutionStateStore;
    private readonly RuntimeCheckpointCommitter _checkpointCommitter;
    private readonly IRuntimeActivityExecutionInspectionAccumulator _inspectionAccumulator;
    private readonly TimeProvider _timeProvider;

    public WorkflowCancelSchedulerWorkHandler(
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        IActivityExecutionStateStore activityExecutionStateStore,
        RuntimeCheckpointCommitter checkpointCommitter,
        IRuntimeActivityExecutionInspectionAccumulator inspectionAccumulator,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(workflowExecutionStateStore);
        ArgumentNullException.ThrowIfNull(activityExecutionStateStore);
        ArgumentNullException.ThrowIfNull(checkpointCommitter);
        ArgumentNullException.ThrowIfNull(inspectionAccumulator);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _workflowExecutionStateStore = workflowExecutionStateStore;
        _activityExecutionStateStore = activityExecutionStateStore;
        _checkpointCommitter = checkpointCommitter;
        _inspectionAccumulator = inspectionAccumulator;
        _timeProvider = timeProvider;
    }

    public string Name => HandlerName;

    public bool CanHandle(RuntimeSchedulerWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        return workItem.CommandKind == WorkflowExecutionCommandKind.Cancel;
    }

    /// <summary>Direct (no-pipeline) dispatch: build the cancellation commit and commit it inline.</summary>
    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        var commit = await BuildCommitAsync(workItem, cancellationToken);
        await _checkpointCommitter.CommitAsync(commit, cancellationToken);
    }

    /// <summary>Pipeline dispatch (Move 2): build the commit in the Invoke slot and stage it for the Checkpoint slot.</summary>
    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, IRuntimePipelineContext pipelineContext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pipelineContext);
        pipelineContext.Workspace.PendingCheckpointCommit = await BuildCommitAsync(workItem, cancellationToken);
    }

    private async ValueTask<RuntimeCheckpointCommit> BuildCommitAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        var occurredAt = _timeProvider.GetUtcNow();
        var workflowState = await _workflowExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, cancellationToken);
        if (workflowState is null)
            throw new InvalidOperationException($"Cancel scheduler work item '{workItem.WorkItemId}' references missing workflow execution '{workItem.WorkflowExecutionId}'.");

        var cancelledWorkflowState = workflowState with
        {
            Status = WorkflowExecutionStatus.Cancelled,
            UpdatedAt = occurredAt,
            CompletedAt = occurredAt
        };
        var cancellableStates = (await _activityExecutionStateStore.ListAsync(workItem.WorkflowExecutionId, cancellationToken))
            .Where(IsCancellable)
            .Select(state => state with
            {
                Status = ActivityExecutionStatus.Cancelled,
                CompletedAt = occurredAt
            })
            .OrderBy(state => state.ExecutionSequence)
            .ThenBy(state => state.Execution.ActivityExecutionId, StringComparer.Ordinal)
            .ToArray();
        var checkpointId = $"checkpoint:{workItem.WorkItemId}:cancel";
        var stateChangeMetadata = new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId,
            [RuntimeMetadataKeys.CheckpointReason] = CancellationReason
        };
        var activityStateChanges = cancellableStates
            .Select(state => new RuntimeStateChange<ActivityExecutionState>(
                StateId: state.Execution.ActivityExecutionId,
                Operation: RuntimeStateChangeOperation.Upsert,
                State: state,
                Metadata: stateChangeMetadata))
            .ToArray();
        var activityInspectionChanges = new List<RuntimeStateChange<ActivityExecutionInspectionProjection>>();
        foreach (var state in cancellableStates)
        {
            var inspection = await _inspectionAccumulator.BuildProjectionAsync(
                state,
                checkpointId,
                occurredAt,
                metadata: stateChangeMetadata,
                cancellationToken: cancellationToken);
            activityInspectionChanges.Add(new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                StateId: state.Execution.ActivityExecutionId,
                Operation: RuntimeStateChangeOperation.Upsert,
                State: inspection,
                Metadata: stateChangeMetadata));
        }

        var commit = new RuntimeCheckpointCommit(
            CommitId: $"commit:{workItem.WorkItemId}:cancel",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: checkpointId,
                Name: RuntimeCheckpointNames.ActivityCancelled,
                WorkflowExecutionId: workItem.WorkflowExecutionId,
                OccurredAt: occurredAt,
                ActivityExecutionIds: cancellableStates.Select(state => state.Execution.ActivityExecutionId).ToArray(),
                Metadata: new Dictionary<string, string>
                {
                    [RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId,
                    [RuntimeMetadataKeys.CommandId] = workItem.CommandId,
                    [RuntimeMetadataKeys.CheckpointReason] = CancellationReason
                }),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: new RuntimeStateChange<WorkflowExecutionState>(
                    StateId: cancelledWorkflowState.WorkflowExecutionId,
                    Operation: RuntimeStateChangeOperation.Upsert,
                    State: cancelledWorkflowState,
                    Metadata: stateChangeMetadata),
                scheduler: null,
                activityExecutions: activityStateChanges,
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: [],
                activityExecutionInspections: activityInspectionChanges),
            PostCommitIntents: [],
            Metadata: new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId,
                [RuntimeMetadataKeys.CommandKind] = workItem.CommandKind.ToString()
            });

        return commit;
    }

    private static bool IsCancellable(ActivityExecutionState state) =>
        state.Status is not ActivityExecutionStatus.Completed
            and not ActivityExecutionStatus.Faulted
            and not ActivityExecutionStatus.Cancelled
            and not ActivityExecutionStatus.Recovered;
}
