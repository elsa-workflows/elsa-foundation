using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class WorkflowScheduleActivitySchedulerWorkHandler : IWorkflowSchedulerWorkHandler
{
    public const string HandlerName = nameof(WorkflowScheduleActivitySchedulerWorkHandler);

    private readonly IWorkflowExecutableStore _workflowExecutableStore;
    private readonly IActivityExecutionStateStore _activityExecutionStateStore;
    private readonly IWorkflowSchedulerWorkQueue _schedulerWorkQueue;
    private readonly RuntimeCheckpointCommitter? _checkpointCommitter;
    private readonly IRuntimeActivityExecutionInspectionAccumulator? _inspectionAccumulator;
    private readonly TimeProvider _timeProvider;

    public WorkflowScheduleActivitySchedulerWorkHandler(
        IWorkflowExecutableStore workflowExecutableStore,
        IActivityExecutionStateStore activityExecutionStateStore,
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        RuntimeCheckpointCommitter? checkpointCommitter,
        IRuntimeActivityExecutionInspectionAccumulator? inspectionAccumulator,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(workflowExecutableStore);
        ArgumentNullException.ThrowIfNull(activityExecutionStateStore);
        ArgumentNullException.ThrowIfNull(schedulerWorkQueue);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _workflowExecutableStore = workflowExecutableStore;
        _activityExecutionStateStore = activityExecutionStateStore;
        _schedulerWorkQueue = schedulerWorkQueue;
        _checkpointCommitter = checkpointCommitter;
        _inspectionAccumulator = inspectionAccumulator;
        _timeProvider = timeProvider;
    }

    public string Name => HandlerName;

    public bool CanHandle(RuntimeSchedulerWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        return workItem.CommandKind == WorkflowExecutionCommandKind.ScheduleActivity;
    }

    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        var schedulePayload = DeserializeSchedulePayload(workItem);
        var executable = await _workflowExecutableStore.FindAsync(schedulePayload.PinnedExecutable.ArtifactId, cancellationToken);
        if (executable is null)
            throw new WorkflowExecutableNotFoundException(schedulePayload.PinnedExecutable.ArtifactId);

        ValidatePinnedExecutable(workItem, schedulePayload.PinnedExecutable, executable.Identity);

        if (!executable.NodesById.TryGetValue(schedulePayload.ExecutableNodeId, out var executableNode))
            throw new InvalidOperationException($"ScheduleActivity scheduler work item '{workItem.WorkItemId}' references executable node '{schedulePayload.ExecutableNodeId}', which is missing from executable artifact '{WorkflowExecutableIdentityComparer.Format(executable.Identity)}'.");

        var existing = await _activityExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, schedulePayload.ActivityExecutionId, cancellationToken);
        if (existing is not null)
        {
            if (!StringComparer.Ordinal.Equals(existing.Execution.ExecutableNodeId, schedulePayload.ExecutableNodeId))
                throw new InvalidOperationException($"ScheduleActivity scheduler work item '{workItem.WorkItemId}' references executable node '{schedulePayload.ExecutableNodeId}', but activity execution '{schedulePayload.ActivityExecutionId}' belongs to executable node '{existing.Execution.ExecutableNodeId}'.");

            if (existing.Status == ActivityExecutionStatus.Scheduled)
                await EnqueueStartActivityAsync(workItem, schedulePayload, cancellationToken);

            return;
        }

        var state = NewActivityExecutionState(workItem, schedulePayload, executableNode);
        if (_checkpointCommitter is null || _inspectionAccumulator is null)
        {
            await _activityExecutionStateStore.SaveAsync(state, cancellationToken);
            await EnqueueStartActivityAsync(workItem, schedulePayload, cancellationToken);
            return;
        }

        var commit = await NewCommitAsync(workItem, schedulePayload, state, cancellationToken);
        await _checkpointCommitter.CommitAsync(commit, cancellationToken);
    }

    private static RuntimeScheduleActivityCommandPayload DeserializeSchedulePayload(RuntimeSchedulerWorkItem workItem)
    {
        if (workItem.Payload is not { } payload)
            throw new InvalidOperationException("ScheduleActivity scheduler work item requires a schedule activity payload.");

        try
        {
            return payload.Deserialize<RuntimeScheduleActivityCommandPayload>()
                   ?? throw new InvalidOperationException("ScheduleActivity scheduler work item payload resolved to null.");
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException ||
            exception is ArgumentException argumentException && IsSchedulePayloadValidationException(argumentException))
        {
            throw new InvalidOperationException("ScheduleActivity scheduler work item payload is not a valid schedule activity payload.", exception);
        }
    }

    private static bool IsSchedulePayloadValidationException(ArgumentException exception) =>
        exception.ParamName is
            "pinnedExecutable" or
            "executableNodeId" or
            "activityExecutionId" or
            "reason" or
            "schedulingActivityExecutionId" or
            "parentActivityExecutionId";

    private static void ValidatePinnedExecutable(
        RuntimeSchedulerWorkItem workItem,
        WorkflowExecutableIdentity pinnedExecutable,
        WorkflowExecutableIdentity loadedExecutable)
    {
        if (WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(loadedExecutable, pinnedExecutable))
            return;

        throw new InvalidOperationException(
            $"ScheduleActivity scheduler work item '{workItem.WorkItemId}' loaded executable artifact '{WorkflowExecutableIdentityComparer.Format(loadedExecutable)}' " +
            $"but pinned executable artifact '{WorkflowExecutableIdentityComparer.Format(pinnedExecutable)}'.");
    }

    private ActivityExecutionState NewActivityExecutionState(
        RuntimeSchedulerWorkItem workItem,
        RuntimeScheduleActivityCommandPayload schedulePayload,
        ExecutableNode executableNode)
    {
        var scheduledAt = _timeProvider.GetUtcNow();
        var provenance = NormalizeProvenance(workItem.WorkflowExecutionId, schedulePayload);
        var execution = new ActivityExecution(
            ActivityExecutionId: schedulePayload.ActivityExecutionId,
            WorkflowExecutionId: workItem.WorkflowExecutionId,
            ExecutableNodeId: executableNode.ExecutableNodeId,
            AuthoredActivityId: executableNode.AuthoredActivityId,
            ActivityType: executableNode.ActivityType,
            ActivityTypeVersion: executableNode.ActivityTypeVersion);

        return new ActivityExecutionState(
            Execution: execution,
            Status: ActivityExecutionStatus.Scheduled,
            SubStatus: null,
            ExecutionSequence: ResolveExecutionSequence(workItem, schedulePayload.ActivityExecutionId),
            ScheduledAt: scheduledAt,
            StartedAt: null,
            CompletedAt: null,
            SchedulingActivityExecutionId: provenance.SchedulingActivityExecutionId,
            ParentActivityExecutionId: provenance.ParentActivityExecutionId,
            BranchId: provenance.BranchId,
            IterationId: provenance.IterationId,
            Provenance: provenance,
            CallStackDepth: null,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.ScheduleReason] = schedulePayload.Reason,
                [RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId,
                [RuntimeMetadataKeys.PinnedArtifactId] = schedulePayload.PinnedExecutable.ArtifactId
            });
    }

    private async ValueTask<RuntimeCheckpointCommit> NewCommitAsync(
        RuntimeSchedulerWorkItem workItem,
        RuntimeScheduleActivityCommandPayload schedulePayload,
        ActivityExecutionState state,
        CancellationToken cancellationToken)
    {
        var occurredAt = _timeProvider.GetUtcNow();
        var checkpointId = $"checkpoint:{workItem.WorkItemId}:activity-scheduled:{schedulePayload.ActivityExecutionId}";
        var metadata = RuntimeModelMetadata.Snapshot(new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId,
            [RuntimeMetadataKeys.CommandId] = workItem.CommandId,
            [RuntimeMetadataKeys.CheckpointReason] = schedulePayload.Reason,
            [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory,
            [RuntimeMetadataKeys.ActivityExecutionId] = schedulePayload.ActivityExecutionId,
            [RuntimeMetadataKeys.ExecutableNodeId] = schedulePayload.ExecutableNodeId,
            [RuntimeMetadataKeys.ExecutableArtifactId] = schedulePayload.PinnedExecutable.ArtifactId,
            [RuntimeMetadataKeys.ExecutableArtifactVersion] = schedulePayload.PinnedExecutable.ArtifactVersion,
            [RuntimeMetadataKeys.ExecutableArtifactHash] = schedulePayload.PinnedExecutable.ArtifactHash
        });
        var inspection = await _inspectionAccumulator!.BuildProjectionAsync(state, checkpointId, occurredAt, metadata: metadata, cancellationToken: cancellationToken);
        var startWorkItem = NewStartActivityWorkItem(workItem, schedulePayload);

        return new RuntimeCheckpointCommit(
            CommitId: $"commit:{workItem.WorkItemId}:activity-scheduled:{schedulePayload.ActivityExecutionId}",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: checkpointId,
                Name: RuntimeCheckpointNames.ActivityScheduled,
                WorkflowExecutionId: workItem.WorkflowExecutionId,
                OccurredAt: occurredAt,
                ActivityExecutionIds: [schedulePayload.ActivityExecutionId],
                Metadata: metadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions:
                [
                    new RuntimeStateChange<ActivityExecutionState>(
                        StateId: schedulePayload.ActivityExecutionId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: state,
                        Metadata: metadata)
                ],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: [],
                activityExecutionInspections:
                [
                    new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                        StateId: schedulePayload.ActivityExecutionId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: inspection,
                        Metadata: metadata)
                ]),
            PostCommitIntents: [NewEnqueueSchedulerWorkIntent(workItem, schedulePayload.ActivityExecutionId, startWorkItem, occurredAt)],
            Metadata: metadata);
    }

    private async ValueTask EnqueueStartActivityAsync(
        RuntimeSchedulerWorkItem scheduleWorkItem,
        RuntimeScheduleActivityCommandPayload schedulePayload,
        CancellationToken cancellationToken)
    {
        var workItem = NewStartActivityWorkItem(scheduleWorkItem, schedulePayload);
        await _schedulerWorkQueue.EnqueueAsync(workItem, cancellationToken);
    }

    private RuntimeSchedulerWorkItem NewStartActivityWorkItem(
        RuntimeSchedulerWorkItem scheduleWorkItem,
        RuntimeScheduleActivityCommandPayload schedulePayload)
    {
        var now = _timeProvider.GetUtcNow();
        var payload = new RuntimeStartActivityCommandPayload(
            schedulePayload.PinnedExecutable,
            schedulePayload.ExecutableNodeId,
            schedulePayload.ActivityExecutionId,
            RuntimeStartActivityCommandPayload.ScheduledActivityReason);

        return new RuntimeSchedulerWorkItem(
            workItemId: $"{scheduleWorkItem.WorkItemId}:start:{schedulePayload.ActivityExecutionId}",
            workflowExecutionId: scheduleWorkItem.WorkflowExecutionId,
            commandId: $"{scheduleWorkItem.CommandId}:start:{schedulePayload.ActivityExecutionId}",
            commandKind: WorkflowExecutionCommandKind.StartActivity,
            envelopeId: scheduleWorkItem.EnvelopeId,
            idempotencyKey: $"{scheduleWorkItem.IdempotencyKey}:start:{schedulePayload.ActivityExecutionId}",
            enqueuedAt: now,
            recordedAt: now,
            sequence: scheduleWorkItem.Sequence is { } sequence ? sequence + 1 : null,
            payload: JsonSerializer.SerializeToElement(payload),
            commandMetadata: scheduleWorkItem.CommandMetadata,
            envelopeMetadata: scheduleWorkItem.EnvelopeMetadata);
    }

    private static RuntimePostCommitIntent NewEnqueueSchedulerWorkIntent(
        RuntimeSchedulerWorkItem sourceWorkItem,
        string activityExecutionId,
        RuntimeSchedulerWorkItem schedulerWorkItem,
        DateTimeOffset recordedAt) =>
        new(
            intentId: $"{sourceWorkItem.WorkItemId}:post-commit:{schedulerWorkItem.WorkItemId}",
            workflowExecutionId: sourceWorkItem.WorkflowExecutionId,
            kind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork,
            recordedAt: recordedAt,
            activityExecutionId: activityExecutionId,
            idempotencyKey: $"{sourceWorkItem.IdempotencyKey}:post-commit:{schedulerWorkItem.IdempotencyKey}",
            payload: JsonSerializer.SerializeToElement(schedulerWorkItem),
            metadata: sourceWorkItem.CommandMetadata);

    private static ActivitySchedulingProvenance NormalizeProvenance(
        string workflowExecutionId,
        RuntimeScheduleActivityCommandPayload schedulePayload)
    {
        var provenance = schedulePayload.SchedulingProvenance;
        return ActivitySchedulingProvenance.From(
            workflowExecutionId,
            provenance.ParentActivityExecutionId ?? schedulePayload.ParentActivityExecutionId,
            provenance.SchedulingActivityExecutionId ?? schedulePayload.SchedulingActivityExecutionId,
            provenance.BranchId,
            provenance.IterationId,
            provenance.ExecutionPathId,
            provenance.ExecutionScopeId ?? ReadMetadata(provenance.Metadata, RuntimeMetadataKeys.FlowchartExecutionScopeId),
            provenance.SchedulingCause ?? schedulePayload.Reason,
            provenance.Metadata);
    }

    private static string? ReadMetadata(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static long ResolveExecutionSequence(RuntimeSchedulerWorkItem workItem, string activityExecutionId) =>
        workItem.Sequence ?? StableHash(activityExecutionId);

    private static long StableHash(string value)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offset;
        foreach (var character in value)
        {
            hash ^= character;
            hash *= prime;
        }

        return (long)(hash & long.MaxValue);
    }
}
