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
    private readonly WorkflowCancellationPlanner _cancellationPlanner;

    public WorkflowCancelSchedulerWorkHandler(
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        IActivityExecutionStateStore activityExecutionStateStore,
        RuntimeCheckpointCommitter checkpointCommitter,
        IRuntimeActivityExecutionInspectionAccumulator inspectionAccumulator,
        TimeProvider timeProvider,
        IActivityScopeCleanupStore? activityScopeCleanupStore = null)
    {
        ArgumentNullException.ThrowIfNull(workflowExecutionStateStore);
        ArgumentNullException.ThrowIfNull(activityExecutionStateStore);
        ArgumentNullException.ThrowIfNull(checkpointCommitter);
        ArgumentNullException.ThrowIfNull(inspectionAccumulator);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _workflowExecutionStateStore = workflowExecutionStateStore;
        _activityExecutionStateStore = activityExecutionStateStore;
        _checkpointCommitter = checkpointCommitter;
        _cancellationPlanner = new WorkflowCancellationPlanner(inspectionAccumulator, timeProvider, activityScopeCleanupStore);
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
        if (commit is not null)
            await _checkpointCommitter.CommitAsync(commit, cancellationToken);
    }

    /// <summary>Pipeline dispatch (Move 2): build the commit in the Invoke slot and stage it for the Checkpoint slot.</summary>
    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, IRuntimePipelineContext pipelineContext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pipelineContext);
        var commit = await BuildCommitAsync(workItem, cancellationToken);
        if (commit is not null)
            pipelineContext.Workspace.PendingCheckpointCommit = commit;
    }

    private async ValueTask<RuntimeCheckpointCommit?> BuildCommitAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        var workflowState = await _workflowExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, cancellationToken);
        if (workflowState is null)
            throw new InvalidOperationException($"Cancel scheduler work item '{workItem.WorkItemId}' references missing workflow execution '{workItem.WorkflowExecutionId}'.");

        var checkpointId = $"checkpoint:{workItem.WorkItemId}:cancel";
        var stateChangeMetadata = new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId,
            [RuntimeMetadataKeys.CheckpointReason] = CancellationReason
        };
        var plan = await _cancellationPlanner.PlanAsync(
            workflowState,
            await _activityExecutionStateStore.ListAllAsync(workItem.WorkflowExecutionId, cancellationToken),
            checkpointId,
            stateChangeMetadata,
            cancellationToken);
        if (plan is null)
            return null;

        var commit = new RuntimeCheckpointCommit(
            CommitId: $"commit:{workItem.WorkItemId}:cancel",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: checkpointId,
                Name: RuntimeCheckpointNames.ActivityCancelled,
                WorkflowExecutionId: workItem.WorkflowExecutionId,
                OccurredAt: plan.WorkflowExecution.CompletedAt!.Value,
                ActivityExecutionIds: plan.ActivityExecutions.Select(state => state.StateId).ToArray(),
                Metadata: new Dictionary<string, string>
                {
                    [RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId,
                    [RuntimeMetadataKeys.CommandId] = workItem.CommandId,
                    [RuntimeMetadataKeys.CheckpointReason] = CancellationReason
                }),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: new RuntimeStateChange<WorkflowExecutionState>(
                    StateId: plan.WorkflowExecution.WorkflowExecutionId,
                    Operation: RuntimeStateChangeOperation.Upsert,
                    State: plan.WorkflowExecution,
                    Metadata: stateChangeMetadata),
                scheduler: null,
                activityExecutions: plan.ActivityExecutions,
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: [],
                activityExecutionInspections: plan.ActivityExecutionInspections),
            PostCommitIntents: [],
            Metadata: new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId,
                [RuntimeMetadataKeys.CommandKind] = workItem.CommandKind.ToString()
            });

        return commit;
    }
}
