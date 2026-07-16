using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Commits an activity-authored cancellation as a terminal invocation transition and atomically
/// schedules workflow cancellation. The activity checkpoint remains the authoritative evidence for
/// the returned transition; the follow-up command terminates the surrounding run and its live work.
/// </summary>
internal static class ActivityCancellationCheckpointService
{
    private const string CancellationSubStatus = "ActivityReturnedCancel";

    public static async ValueTask CommitAsync(
        RuntimeCheckpointCommitter checkpointCommitter,
        IRuntimeActivityExecutionInspectionAccumulator? inspectionAccumulator,
        TimeProvider timeProvider,
        RuntimeSchedulerWorkItem sourceWorkItem,
        ActivityExecutionState state,
        string reason,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots,
        BookmarkState? consumedBookmark = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpointCommitter);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(sourceWorkItem);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var occurredAt = timeProvider.GetUtcNow();
        var metadata = state.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[RuntimeMetadataKeys.SchedulerWorkItemId] = sourceWorkItem.WorkItemId;
        metadata[RuntimeMetadataKeys.CommandId] = sourceWorkItem.CommandId;
        metadata[RuntimeMetadataKeys.ActivityExecutionId] = state.InvocationId;
        metadata[RuntimeMetadataKeys.ExecutableNodeId] = state.Execution.ExecutableNodeId;
        metadata[RuntimeMetadataKeys.CheckpointReason] = CancellationSubStatus;
        metadata[RuntimeMetadataKeys.CancellationReason] = reason;
        metadata[RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory;

        var cancelledState = RuntimeContainerScopeService.CloseOwnedFrames(EndOpenAttempt(state, occurredAt) with
        {
            Status = ActivityExecutionStatus.Cancelled,
            SubStatus = CancellationSubStatus,
            CompletedAt = occurredAt,
            Completion = null,
            Metadata = metadata
        });
        var checkpointId = $"checkpoint:{sourceWorkItem.WorkItemId}:activity-cancelled:{state.InvocationId}";
        var inspection = inspectionAccumulator is null
            ? null
            : await inspectionAccumulator.BuildProjectionAsync(
                cancelledState,
                checkpointId,
                occurredAt,
                valueSnapshots: valueSnapshots,
                metadata: metadata,
                cancellationToken: cancellationToken);
        var workflowCancelWorkItem = NewWorkflowCancelWorkItem(sourceWorkItem, state.InvocationId, reason, occurredAt);
        var commit = new RuntimeCheckpointCommit(
            CommitId: $"commit:{sourceWorkItem.WorkItemId}:activity-cancelled:{state.InvocationId}",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: checkpointId,
                Name: RuntimeCheckpointNames.ActivityCancelled,
                WorkflowExecutionId: sourceWorkItem.WorkflowExecutionId,
                OccurredAt: occurredAt,
                ActivityExecutionIds: [state.InvocationId],
                Metadata: metadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions:
                [
                    new RuntimeStateChange<ActivityExecutionState>(
                        state.InvocationId,
                        RuntimeStateChangeOperation.Upsert,
                        cancelledState,
                        metadata)
                ],
                bookmarks: consumedBookmark is null
                    ? []
                    :
                    [
                        new RuntimeStateChange<BookmarkState>(
                            consumedBookmark.BookmarkId,
                            RuntimeStateChangeOperation.Delete,
                            consumedBookmark,
                            metadata)
                    ],
                durableValues: [],
                incidents: [],
                operational: [],
                activityExecutionInspections: inspection is null
                    ? []
                    :
                    [
                        new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                            state.InvocationId,
                            RuntimeStateChangeOperation.Upsert,
                            inspection,
                            metadata)
                    ]),
            PostCommitIntents:
            [
                SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent(
                    sourceWorkItem,
                    state.InvocationId,
                    workflowCancelWorkItem,
                    occurredAt)
            ],
            Metadata: metadata);

        await checkpointCommitter.CommitAsync(commit, cancellationToken);
    }

    private static ActivityExecutionState EndOpenAttempt(ActivityExecutionState state, DateTimeOffset endedAt)
    {
        var attempts = state.Attempts ?? [];
        var openAttempt = attempts
            .Where(attempt => attempt.EndedAt is null)
            .OrderByDescending(attempt => attempt.Ordinal)
            .FirstOrDefault();
        if (openAttempt is null)
            return state;

        var endedAttempt = new ActivityAttempt(
            openAttempt.AttemptId,
            openAttempt.InvocationId,
            openAttempt.Ordinal,
            openAttempt.Reason,
            openAttempt.StartedAt,
            endedAt,
            openAttempt.TriggerDeliveryId,
            ActivityTransitionKind.Cancel);
        return state with
        {
            Attempts = attempts
                .Where(attempt => attempt.AttemptId != openAttempt.AttemptId)
                .Append(endedAttempt)
                .OrderBy(attempt => attempt.Ordinal)
                .ToArray()
        };
    }

    private static RuntimeSchedulerWorkItem NewWorkflowCancelWorkItem(
        RuntimeSchedulerWorkItem source,
        string activityExecutionId,
        string reason,
        DateTimeOffset occurredAt) =>
        new(
            workItemId: $"{source.WorkItemId}:cancel-workflow",
            workflowExecutionId: source.WorkflowExecutionId,
            commandId: $"{source.CommandId}:cancel-workflow",
            commandKind: WorkflowExecutionCommandKind.Cancel,
            envelopeId: source.EnvelopeId,
            idempotencyKey: $"{source.IdempotencyKey}:cancel-workflow",
            enqueuedAt: occurredAt,
            recordedAt: occurredAt,
            sequence: source.Sequence is { } sequence ? sequence + 1 : null,
            payload: null,
            commandMetadata: new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.ActivityExecutionId] = activityExecutionId,
                [RuntimeMetadataKeys.CancellationReason] = reason
            },
            envelopeMetadata: source.EnvelopeMetadata);
}
