using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class WorkflowCheckpointSchedulerWorkHandler : IWorkflowSchedulerWorkHandler
{
    public const string HandlerName = nameof(WorkflowCheckpointSchedulerWorkHandler);

    private readonly IActivityExecutionStateStore _activityExecutionStateStore;
    private readonly RuntimeCheckpointCommitter _checkpointCommitter;
    private readonly TimeProvider _timeProvider;

    public WorkflowCheckpointSchedulerWorkHandler(
        IActivityExecutionStateStore activityExecutionStateStore,
        RuntimeCheckpointCommitter checkpointCommitter)
        : this(activityExecutionStateStore, checkpointCommitter, TimeProvider.System)
    {
    }

    public WorkflowCheckpointSchedulerWorkHandler(
        IActivityExecutionStateStore activityExecutionStateStore,
        RuntimeCheckpointCommitter checkpointCommitter,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(activityExecutionStateStore);
        ArgumentNullException.ThrowIfNull(checkpointCommitter);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _activityExecutionStateStore = activityExecutionStateStore;
        _checkpointCommitter = checkpointCommitter;
        _timeProvider = timeProvider;
    }

    public string Name => HandlerName;

    public bool CanHandle(RuntimeSchedulerWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        return workItem.CommandKind == WorkflowExecutionCommandKind.Checkpoint;
    }

    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        var payload = DeserializeCheckpointPayload(workItem);
        var commit = await BuildCommitAsync(workItem, payload, cancellationToken);
        await _checkpointCommitter.CommitAsync(commit, cancellationToken);
    }

    private async ValueTask<RuntimeCheckpointCommit> BuildCommitAsync(
        RuntimeSchedulerWorkItem workItem,
        RuntimeCheckpointCommandPayload payload,
        CancellationToken cancellationToken)
    {
        var occurredAt = _timeProvider.GetUtcNow();
        var activityStateChanges = new List<RuntimeStateChange<ActivityExecutionState>>();
        var activityStateChangeMetadata = RuntimeModelMetadata.Snapshot(new Dictionary<string, string>
        {
            ["runtime.schedulerWorkItemId"] = workItem.WorkItemId,
            ["runtime.checkpointReason"] = payload.Reason
        });

        foreach (var activityExecutionId in payload.ActivityExecutionIds)
        {
            var state = await _activityExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, activityExecutionId, cancellationToken);
            if (state is null)
                throw new InvalidOperationException($"Checkpoint scheduler work item '{workItem.WorkItemId}' references missing activity execution '{activityExecutionId}' for workflow execution '{workItem.WorkflowExecutionId}'.");

            activityStateChanges.Add(new RuntimeStateChange<ActivityExecutionState>(
                StateId: activityExecutionId,
                Operation: RuntimeStateChangeOperation.Upsert,
                State: state,
                Metadata: activityStateChangeMetadata));
        }

        return new RuntimeCheckpointCommit(
            CommitId: $"commit:{workItem.WorkItemId}",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: $"checkpoint:{workItem.WorkItemId}",
                Name: payload.CheckpointName,
                WorkflowExecutionId: workItem.WorkflowExecutionId,
                OccurredAt: occurredAt,
                ActivityExecutionIds: payload.ActivityExecutionIds,
                Metadata: RuntimeModelMetadata.Snapshot(new Dictionary<string, string>
                {
                    ["runtime.schedulerWorkItemId"] = workItem.WorkItemId,
                    ["runtime.commandId"] = workItem.CommandId,
                    ["runtime.checkpointReason"] = payload.Reason,
                    ["runtime.executableArtifactId"] = payload.PinnedExecutable.ArtifactId,
                    ["runtime.executableArtifactVersion"] = payload.PinnedExecutable.ArtifactVersion,
                    ["runtime.executableArtifactHash"] = payload.PinnedExecutable.ArtifactHash
                })),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: BuildWorkflowExecutionStateChange(workItem, payload, occurredAt),
                scheduler: null,
                activityExecutions: activityStateChanges.ToArray(),
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: []),
            PostCommitIntents: payload.PostCommitIntents.ToArray(),
            Metadata: RuntimeModelMetadata.Snapshot(new Dictionary<string, string>
            {
                ["runtime.schedulerWorkItemId"] = workItem.WorkItemId,
                ["runtime.commandKind"] = workItem.CommandKind.ToString()
            }));
    }

    private static RuntimeStateChange<WorkflowExecutionState>? BuildWorkflowExecutionStateChange(
        RuntimeSchedulerWorkItem workItem,
        RuntimeCheckpointCommandPayload payload,
        DateTimeOffset occurredAt)
    {
        if (StringComparer.Ordinal.Equals(payload.CheckpointName, RuntimeCheckpointNames.WorkflowStarted))
            return BuildWorkflowStartedStateChange(workItem, payload, occurredAt);

        if (StringComparer.Ordinal.Equals(payload.CheckpointName, RuntimeCheckpointNames.WorkflowCompleted))
            return BuildWorkflowCompletedStateChange(workItem, payload, occurredAt);

        return null;
    }

    private static RuntimeStateChange<WorkflowExecutionState> BuildWorkflowStartedStateChange(
        RuntimeSchedulerWorkItem workItem,
        RuntimeCheckpointCommandPayload payload,
        DateTimeOffset occurredAt)
    {
        var state = new WorkflowExecutionState(
            WorkflowExecutionId: workItem.WorkflowExecutionId,
            PinnedExecutable: payload.PinnedExecutable,
            Status: WorkflowExecutionStatus.Running,
            SubStatus: null,
            CreatedAt: occurredAt,
            StartedAt: occurredAt,
            UpdatedAt: occurredAt,
            CompletedAt: null,
            CorrelationId: null,
            ParentWorkflowExecutionId: null,
            TenantId: null,
            SystemMetadata: RuntimeModelMetadata.Snapshot(new Dictionary<string, string>
            {
                ["runtime.checkpointReason"] = payload.Reason,
                ["runtime.schedulerWorkItemId"] = workItem.WorkItemId
            }));

        return NewWorkflowExecutionStateChange(workItem, payload, state);
    }

    private static RuntimeStateChange<WorkflowExecutionState> BuildWorkflowCompletedStateChange(
        RuntimeSchedulerWorkItem workItem,
        RuntimeCheckpointCommandPayload payload,
        DateTimeOffset occurredAt)
    {
        var state = new WorkflowExecutionState(
            WorkflowExecutionId: workItem.WorkflowExecutionId,
            PinnedExecutable: payload.PinnedExecutable,
            Status: WorkflowExecutionStatus.Completed,
            SubStatus: null,
            // This slice has no workflow execution state store yet, so only terminal timestamps are authoritative.
            CreatedAt: occurredAt,
            StartedAt: null,
            UpdatedAt: occurredAt,
            CompletedAt: occurredAt,
            CorrelationId: null,
            ParentWorkflowExecutionId: null,
            TenantId: null,
            SystemMetadata: RuntimeModelMetadata.Snapshot(new Dictionary<string, string>
            {
                ["runtime.checkpointReason"] = payload.Reason,
                ["runtime.schedulerWorkItemId"] = workItem.WorkItemId
            }));

        return NewWorkflowExecutionStateChange(workItem, payload, state);
    }

    private static RuntimeStateChange<WorkflowExecutionState> NewWorkflowExecutionStateChange(
        RuntimeSchedulerWorkItem workItem,
        RuntimeCheckpointCommandPayload payload,
        WorkflowExecutionState state) =>
        new(
            StateId: workItem.WorkflowExecutionId,
            Operation: RuntimeStateChangeOperation.Upsert,
            State: state,
            Metadata: RuntimeModelMetadata.Snapshot(new Dictionary<string, string>
            {
                ["runtime.schedulerWorkItemId"] = workItem.WorkItemId,
                ["runtime.checkpointReason"] = payload.Reason
            }));

    private static RuntimeCheckpointCommandPayload DeserializeCheckpointPayload(RuntimeSchedulerWorkItem workItem)
    {
        if (workItem.Payload is not { } payload)
            throw new InvalidOperationException("Checkpoint scheduler work item requires a checkpoint payload.");

        try
        {
            return payload.Deserialize<RuntimeCheckpointCommandPayload>()
                   ?? throw new InvalidOperationException("Checkpoint scheduler work item payload resolved to null.");
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException ||
            exception is RuntimeCheckpointCommandPayloadValidationException)
        {
            throw new InvalidOperationException("Checkpoint scheduler work item payload is not a valid checkpoint payload.", exception);
        }
    }
}
