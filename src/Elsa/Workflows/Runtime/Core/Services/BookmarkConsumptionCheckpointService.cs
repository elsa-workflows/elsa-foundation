using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class BookmarkConsumptionCheckpointService : IBookmarkConsumptionCheckpointService
{
    private readonly RuntimeCheckpointCommitter _checkpointCommitter;
    private readonly IRuntimeActivityExecutionInspectionAccumulator? _inspectionAccumulator;
    private readonly TimeProvider _timeProvider;

    public BookmarkConsumptionCheckpointService(RuntimeCheckpointCommitter checkpointCommitter)
        : this(checkpointCommitter, null, TimeProvider.System)
    {
    }

    public BookmarkConsumptionCheckpointService(
        RuntimeCheckpointCommitter checkpointCommitter,
        IRuntimeActivityExecutionInspectionAccumulator inspectionAccumulator)
        : this(checkpointCommitter, inspectionAccumulator, TimeProvider.System)
    {
    }

    public BookmarkConsumptionCheckpointService(
        RuntimeCheckpointCommitter checkpointCommitter,
        TimeProvider timeProvider)
        : this(checkpointCommitter, null, timeProvider)
    {
    }

    public BookmarkConsumptionCheckpointService(
        RuntimeCheckpointCommitter checkpointCommitter,
        IRuntimeActivityExecutionInspectionAccumulator? inspectionAccumulator,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(checkpointCommitter);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _checkpointCommitter = checkpointCommitter;
        _inspectionAccumulator = inspectionAccumulator;
        _timeProvider = timeProvider;
    }

    public async ValueTask<BookmarkConsumptionCheckpointResult> CommitAsync(
        BookmarkConsumptionCheckpointRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var checkpointId = $"checkpoint:{request.ResumeWorkItem.WorkItemId}:bookmark-consumed:{request.Bookmark.BookmarkId}";
        var commitId = $"commit:{request.ResumeWorkItem.WorkItemId}:bookmark-consumed:{request.Bookmark.BookmarkId}";
        var occurredAt = _timeProvider.GetUtcNow();
        var metadata = RuntimeModelMetadata.Snapshot(new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.SchedulerWorkItemId] = request.ResumeWorkItem.WorkItemId,
            [RuntimeMetadataKeys.CommandId] = request.ResumeWorkItem.CommandId,
            [RuntimeMetadataKeys.CheckpointReason] = request.ResumePayload.Reason,
            [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory,
            [RuntimeMetadataKeys.BookmarkId] = request.Bookmark.BookmarkId,
            [RuntimeMetadataKeys.ActivityExecutionId] = request.CompletedActivityExecutionState.Execution.ActivityExecutionId,
            [RuntimeMetadataKeys.ExecutableNodeId] = request.ResumePayload.ExecutableNodeId,
            [RuntimeMetadataKeys.ResumeTargetId] = request.ResumePayload.ResumeTargetId,
            [RuntimeMetadataKeys.StimulusType] = request.ResumePayload.StimulusType,
            [RuntimeMetadataKeys.StimulusHash] = request.ResumePayload.StimulusHash,
            [RuntimeMetadataKeys.ExecutableArtifactId] = request.ResumePayload.PinnedExecutable.ArtifactId,
            [RuntimeMetadataKeys.ExecutableArtifactVersion] = request.ResumePayload.PinnedExecutable.ArtifactVersion,
            [RuntimeMetadataKeys.ExecutableArtifactHash] = request.ResumePayload.PinnedExecutable.ArtifactHash
        });
        var inspection = _inspectionAccumulator is null
            ? null
            : await _inspectionAccumulator.BuildProjectionAsync(
                request.CompletedActivityExecutionState,
                checkpointId,
                occurredAt,
                outcomeNames: ReadCompletionOutcomeNames(request.CompletedActivityExecutionState),
                valueSnapshots: request.ValueSnapshots,
                metadata: metadata,
                cancellationToken: cancellationToken);
        var commit = new RuntimeCheckpointCommit(
            CommitId: commitId,
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: checkpointId,
                Name: RuntimeCheckpointNames.BookmarkConsumed,
                WorkflowExecutionId: request.ResumeWorkItem.WorkflowExecutionId,
                OccurredAt: occurredAt,
                ActivityExecutionIds: [request.CompletedActivityExecutionState.Execution.ActivityExecutionId],
                Metadata: metadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions:
                [
                    new RuntimeStateChange<ActivityExecutionState>(
                        StateId: request.CompletedActivityExecutionState.Execution.ActivityExecutionId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: request.CompletedActivityExecutionState,
                        Metadata: metadata)
                ],
                bookmarks:
                [
                    new RuntimeStateChange<BookmarkState>(
                        StateId: request.Bookmark.BookmarkId,
                        Operation: RuntimeStateChangeOperation.Delete,
                        State: request.Bookmark,
                        Metadata: metadata)
                ],
                durableValues: [],
                incidents: [],
                operational: [],
                activityExecutionInspections: inspection is null
                    ? []
                    :
                    [
                        new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                            StateId: request.CompletedActivityExecutionState.Execution.ActivityExecutionId,
                            Operation: RuntimeStateChangeOperation.Upsert,
                            State: inspection,
                            Metadata: metadata)
                    ]),
            PostCommitIntents: request.CompletionWorkItem is null
                ? []
                : [NewEnqueueSchedulerWorkIntent(request.ResumeWorkItem, request.CompletedActivityExecutionState.Execution.ActivityExecutionId, request.CompletionWorkItem, occurredAt)],
            Metadata: metadata);

        var decision = await _checkpointCommitter.CommitAsync(commit, cancellationToken);
        return new BookmarkConsumptionCheckpointResult(commitId, checkpointId, decision);
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

    private static IReadOnlyCollection<string> ReadCompletionOutcomeNames(ActivityExecutionState completedState)
    {
        if (completedState.Metadata.TryGetValue(RuntimeMetadataKeys.CompletionOutcomeNames, out var serializedOutcomeNames))
        {
            var outcomeNames = JsonSerializer.Deserialize<string[]>(serializedOutcomeNames)
                ?? throw new InvalidOperationException("Persisted completion outcome names resolved to null.");

            return outcomeNames;
        }

        return [ActivityOutcomes.Done];
    }
}
