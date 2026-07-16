using System.Globalization;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class WorkflowCheckpointSchedulerWorkHandler : IWorkflowSchedulerWorkHandler, IRuntimePipelineWorkHandler
{
    public const string HandlerName = nameof(WorkflowCheckpointSchedulerWorkHandler);

    private readonly IActivityExecutionStateStore _activityExecutionStateStore;
    private readonly RuntimeCheckpointCommitter _checkpointCommitter;
    private readonly IRuntimeActivityExecutionInspectionAccumulator? _inspectionAccumulator;
    private readonly IWorkflowExecutionStateStore? _workflowExecutionStateStore;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Constructs the handler. <paramref name="workflowExecutionStateStore"/> is optional: when supplied (the
    /// DI default — it is a registered service), the handler preserves durable instance fields (correlation id,
    /// parent, tenant) across the workflow-started/completed transitions; when null it falls back to the prior
    /// behaviour of rebuilding workflow state from the checkpoint payload alone.
    /// </summary>
    public WorkflowCheckpointSchedulerWorkHandler(
        IActivityExecutionStateStore activityExecutionStateStore,
        RuntimeCheckpointCommitter checkpointCommitter,
        IRuntimeActivityExecutionInspectionAccumulator? inspectionAccumulator,
        TimeProvider timeProvider,
        IWorkflowExecutionStateStore? workflowExecutionStateStore = null)
    {
        ArgumentNullException.ThrowIfNull(activityExecutionStateStore);
        ArgumentNullException.ThrowIfNull(checkpointCommitter);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _activityExecutionStateStore = activityExecutionStateStore;
        _checkpointCommitter = checkpointCommitter;
        _inspectionAccumulator = inspectionAccumulator;
        _workflowExecutionStateStore = workflowExecutionStateStore;
        _timeProvider = timeProvider;
    }

    public string Name => HandlerName;

    public bool CanHandle(RuntimeSchedulerWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        return workItem.CommandKind == WorkflowExecutionCommandKind.Checkpoint;
    }

    /// <summary>Direct (no-pipeline) dispatch: build the checkpoint commit and commit it inline.</summary>
    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        var payload = DeserializeCheckpointPayload(workItem);
        var commit = await BuildCommitAsync(workItem, payload, cancellationToken);
        await _checkpointCommitter.CommitAsync(commit, cancellationToken);
    }

    /// <summary>Pipeline dispatch (Move 2): build the commit in the Invoke slot and stage it for the Checkpoint slot.</summary>
    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, IRuntimePipelineContext pipelineContext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        ArgumentNullException.ThrowIfNull(pipelineContext);
        cancellationToken.ThrowIfCancellationRequested();

        var payload = DeserializeCheckpointPayload(workItem);
        pipelineContext.Workspace.PendingCheckpointCommit = await BuildCommitAsync(workItem, payload, cancellationToken);
    }

    private async ValueTask<RuntimeCheckpointCommit> BuildCommitAsync(
        RuntimeSchedulerWorkItem workItem,
        RuntimeCheckpointCommandPayload payload,
        CancellationToken cancellationToken)
    {
        var occurredAt = _timeProvider.GetUtcNow();
        var checkpointId = $"checkpoint:{workItem.WorkItemId}";
        var commitId = $"commit:{workItem.WorkItemId}";
        var activityStateChanges = new List<RuntimeStateChange<ActivityExecutionState>>();
        var activityInspectionChanges = new List<RuntimeStateChange<ActivityExecutionInspectionProjection>>();
        var activityStateChangeMetadata = RuntimeModelMetadata.Snapshot(new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId,
            [RuntimeMetadataKeys.CheckpointReason] = payload.Reason
        });

        foreach (var activityExecutionId in payload.ActivityExecutionIds)
        {
            var state = await _activityExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, activityExecutionId, cancellationToken);
            if (state is null)
                throw new InvalidOperationException($"Checkpoint scheduler work item '{workItem.WorkItemId}' references missing activity execution '{activityExecutionId}' for workflow execution '{workItem.WorkflowExecutionId}'.");
            ValidateTerminalCheckpointStatus(workItem, payload, state);

            activityStateChanges.Add(new RuntimeStateChange<ActivityExecutionState>(
                StateId: activityExecutionId,
                Operation: RuntimeStateChangeOperation.Upsert,
                State: state,
                Metadata: activityStateChangeMetadata));
            if (_inspectionAccumulator is not null)
            {
                var inspection = await _inspectionAccumulator.BuildProjectionAsync(
                    state,
                    checkpointId,
                    occurredAt,
                    metadata: activityStateChangeMetadata,
                    cancellationToken: cancellationToken);
                activityInspectionChanges.Add(new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                    StateId: activityExecutionId,
                    Operation: RuntimeStateChangeOperation.Upsert,
                    State: inspection,
                    Metadata: activityStateChangeMetadata));
            }
        }

        var checkpointMetadata = RuntimeModelMetadata.Snapshot(new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId,
            [RuntimeMetadataKeys.CommandId] = workItem.CommandId,
            [RuntimeMetadataKeys.CheckpointReason] = payload.Reason,
            [RuntimeMetadataKeys.ExecutableArtifactId] = payload.PinnedExecutable.ArtifactId,
            [RuntimeMetadataKeys.ExecutableArtifactVersion] = payload.PinnedExecutable.ArtifactVersion,
            [RuntimeMetadataKeys.ExecutableArtifactHash] = payload.PinnedExecutable.ArtifactHash
        });

        // Preserve durable instance fields (correlation id, parent, tenant) that earlier checkpoints set —
        // e.g. a Correlate leaf — across the workflow-completed transition, which otherwise rebuilds the
        // workflow state from the checkpoint payload alone.
        var priorWorkflowState = _workflowExecutionStateStore is null
            ? null
            : await _workflowExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, cancellationToken);

        return new RuntimeCheckpointCommit(
            CommitId: commitId,
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: checkpointId,
                Name: payload.CheckpointName,
                WorkflowExecutionId: workItem.WorkflowExecutionId,
                OccurredAt: occurredAt,
                ActivityExecutionIds: payload.ActivityExecutionIds,
                Metadata: checkpointMetadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: BuildWorkflowExecutionStateChange(workItem, payload, occurredAt, priorWorkflowState),
                scheduler: null,
                activityExecutions: activityStateChanges.ToArray(),
                bookmarks: [],
                durableValues: BuildSeedDurableValueChanges(workItem, payload, occurredAt),
                incidents: [],
                operational: [],
                activityExecutionInspections: activityInspectionChanges.ToArray()),
            PostCommitIntents: payload.PostCommitIntents.ToArray(),
            Metadata: RuntimeModelMetadata.Snapshot(new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId,
                [RuntimeMetadataKeys.CommandKind] = workItem.CommandKind.ToString()
            }));
    }

    private static IReadOnlyCollection<RuntimeStateChange<DurableValueState>> BuildSeedDurableValueChanges(
        RuntimeSchedulerWorkItem workItem,
        RuntimeCheckpointCommandPayload payload,
        DateTimeOffset occurredAt)
    {
        if (payload.SeedVariables.Count == 0 && payload.SeedInputs.Count == 0 && payload.SeedStimulusInput is null && string.IsNullOrWhiteSpace(payload.SeedTriggerNodeId))
            return [];

        return RuntimeWorkflowStateSeed.BuildSeedChanges(
            workItem.WorkflowExecutionId,
            payload.SeedVariables.ToDictionary(item => item.Key, item => (object?)item.Value, StringComparer.Ordinal),
            payload.SeedInputs.ToDictionary(item => item.Key, item => (object?)item.Value, StringComparer.Ordinal),
            occurredAt,
            stimulusInput: payload.SeedStimulusInput,
            triggerNodeId: payload.SeedTriggerNodeId);
    }

    private static void ValidateTerminalCheckpointStatus(
        RuntimeSchedulerWorkItem workItem,
        RuntimeCheckpointCommandPayload payload,
        ActivityExecutionState state)
    {
        if (StringComparer.Ordinal.Equals(payload.CheckpointName, RuntimeCheckpointNames.ActivityCancelled) &&
            state.Status != ActivityExecutionStatus.Cancelled)
            throw new InvalidOperationException($"Checkpoint scheduler work item '{workItem.WorkItemId}' cannot commit '{RuntimeCheckpointNames.ActivityCancelled}' for activity execution '{state.Execution.ActivityExecutionId}' with status '{state.Status}'.");

        if (StringComparer.Ordinal.Equals(payload.CheckpointName, RuntimeCheckpointNames.ActivityRecovered) &&
            state.Status != ActivityExecutionStatus.Recovered)
            throw new InvalidOperationException($"Checkpoint scheduler work item '{workItem.WorkItemId}' cannot commit '{RuntimeCheckpointNames.ActivityRecovered}' for activity execution '{state.Execution.ActivityExecutionId}' with status '{state.Status}'.");
    }

    private static RuntimeStateChange<WorkflowExecutionState>? BuildWorkflowExecutionStateChange(
        RuntimeSchedulerWorkItem workItem,
        RuntimeCheckpointCommandPayload payload,
        DateTimeOffset occurredAt,
        WorkflowExecutionState? priorWorkflowState)
    {
        if (StringComparer.Ordinal.Equals(payload.CheckpointName, RuntimeCheckpointNames.WorkflowStarted))
            return BuildWorkflowStartedStateChange(workItem, payload, occurredAt, priorWorkflowState);

        if (StringComparer.Ordinal.Equals(payload.CheckpointName, RuntimeCheckpointNames.WorkflowCompleted))
            return BuildWorkflowCompletedStateChange(workItem, payload, occurredAt, priorWorkflowState);

        return null;
    }

    private static RuntimeStateChange<WorkflowExecutionState> BuildWorkflowStartedStateChange(
        RuntimeSchedulerWorkItem workItem,
        RuntimeCheckpointCommandPayload payload,
        DateTimeOffset occurredAt,
        WorkflowExecutionState? priorWorkflowState)
    {
        var startedAt = ReadWorkflowStartedAt(workItem) ?? occurredAt;
        var state = new WorkflowExecutionState(
            WorkflowExecutionId: workItem.WorkflowExecutionId,
            PinnedExecutable: payload.PinnedExecutable,
            Status: WorkflowExecutionStatus.Running,
            SubStatus: null,
            CreatedAt: startedAt,
            StartedAt: startedAt,
            UpdatedAt: occurredAt,
            CompletedAt: null,
            CorrelationId: priorWorkflowState?.CorrelationId ?? payload.CorrelationId,
            ParentWorkflowExecutionId: priorWorkflowState?.ParentWorkflowExecutionId ?? payload.ParentWorkflowExecutionId,
            TenantId: priorWorkflowState?.TenantId ?? payload.TenantId,
            SystemMetadata: RuntimeModelMetadata.Snapshot(PreserveInstanceName(new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.CheckpointReason] = payload.Reason,
                [RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId
            }, priorWorkflowState)))
        {
            RunKind = priorWorkflowState?.RunKind ?? payload.RunKind,
            PinnedSource = priorWorkflowState?.PinnedSource ?? payload.PinnedSource,
            Partition = priorWorkflowState?.Partition ?? payload.Partition,
            Authority = priorWorkflowState?.Authority ?? payload.Authority,
            DispatchNestingDepth = priorWorkflowState?.DispatchNestingDepth ?? payload.DispatchNestingDepth
        };

        return NewWorkflowExecutionStateChange(workItem, payload, state);
    }

    // Carries the workflow instance name (#260) forward across a checkpoint rebuild. The completion/running
    // builders rebuild SystemMetadata from scratch, so without this a SetName assignment folded into an
    // activity-completed checkpoint would be wiped by the subsequent workflow-completed checkpoint — mirroring
    // how the dedicated CorrelationId field is carried forward via priorWorkflowState.
    private static Dictionary<string, string> PreserveInstanceName(Dictionary<string, string> metadata, WorkflowExecutionState? priorWorkflowState)
    {
        if (priorWorkflowState?.SystemMetadata.TryGetValue(RuntimeMetadataKeys.InstanceName, out var instanceName) == true)
            metadata[RuntimeMetadataKeys.InstanceName] = instanceName;

        return metadata;
    }

    private static RuntimeStateChange<WorkflowExecutionState> BuildWorkflowCompletedStateChange(
        RuntimeSchedulerWorkItem workItem,
        RuntimeCheckpointCommandPayload payload,
        DateTimeOffset occurredAt,
        WorkflowExecutionState? priorWorkflowState)
    {
        var startedAt = ReadWorkflowStartedAt(workItem) ?? priorWorkflowState?.StartedAt;
        var state = new WorkflowExecutionState(
            WorkflowExecutionId: workItem.WorkflowExecutionId,
            PinnedExecutable: payload.PinnedExecutable,
            Status: WorkflowExecutionStatus.Completed,
            SubStatus: null,
            CreatedAt: priorWorkflowState?.CreatedAt ?? startedAt ?? occurredAt,
            StartedAt: startedAt,
            UpdatedAt: occurredAt,
            CompletedAt: occurredAt,
            CorrelationId: priorWorkflowState?.CorrelationId,
            ParentWorkflowExecutionId: priorWorkflowState?.ParentWorkflowExecutionId,
            TenantId: priorWorkflowState?.TenantId,
            SystemMetadata: RuntimeModelMetadata.Snapshot(PreserveInstanceName(new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.CheckpointReason] = payload.Reason,
                [RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId
            }, priorWorkflowState)))
        {
            RunKind = priorWorkflowState?.RunKind ?? payload.RunKind,
            PinnedSource = priorWorkflowState?.PinnedSource ?? payload.PinnedSource,
            Partition = priorWorkflowState?.Partition ?? payload.Partition,
            Authority = priorWorkflowState?.Authority ?? payload.Authority,
            DispatchNestingDepth = priorWorkflowState?.DispatchNestingDepth ?? payload.DispatchNestingDepth
        };

        return NewWorkflowExecutionStateChange(workItem, payload, state);
    }

    private static DateTimeOffset? ReadWorkflowStartedAt(RuntimeSchedulerWorkItem workItem)
    {
        if (!workItem.CommandMetadata.TryGetValue(RuntimeMetadataKeys.WorkflowStartedAt, out var value))
            return null;

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var startedAt)
            ? startedAt
            : null;
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
                [RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId,
                [RuntimeMetadataKeys.CheckpointReason] = payload.Reason
            }));

    private static RuntimeCheckpointCommandPayload DeserializeCheckpointPayload(RuntimeSchedulerWorkItem workItem) =>
        SchedulerWorkHandlerHelpers.DeserializePayload(
            workItem,
            requiresPayloadMessage: "Checkpoint scheduler work item requires a checkpoint payload.",
            resolvedToNullMessage: "Checkpoint scheduler work item payload resolved to null.",
            invalidPayloadMessage: "Checkpoint scheduler work item payload is not a valid checkpoint payload.",
            deserialize: static (_, payload) => payload.Deserialize<RuntimeCheckpointCommandPayload>(),
            isPayloadValidationException: static exception =>
                exception is JsonException or NotSupportedException or RuntimeCheckpointCommandPayloadValidationException);
}
