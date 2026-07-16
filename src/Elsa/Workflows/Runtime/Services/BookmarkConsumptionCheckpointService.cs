using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class BookmarkConsumptionCheckpointService : IBookmarkConsumptionCheckpointService
{
    private readonly RuntimeCheckpointCommitter _checkpointCommitter;
    private readonly IRuntimeActivityExecutionInspectionAccumulator? _inspectionAccumulator;
    private readonly BookmarkLifecycleNotifier? _bookmarkLifecycleNotifier;
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
        TimeProvider timeProvider,
        BookmarkLifecycleNotifier? bookmarkLifecycleNotifier = null)
    {
        ArgumentNullException.ThrowIfNull(checkpointCommitter);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _checkpointCommitter = checkpointCommitter;
        _inspectionAccumulator = inspectionAccumulator;
        _bookmarkLifecycleNotifier = bookmarkLifecycleNotifier;
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
            [RuntimeMetadataKeys.ActivityExecutionId] = request.ActivityExecutionState.Execution.ActivityExecutionId,
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
                request.ActivityExecutionState,
                checkpointId,
                occurredAt,
                outcomeNames: ReadOutcomeNames(request.ActivityExecutionState),
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
                ActivityExecutionIds: [request.ActivityExecutionState.Execution.ActivityExecutionId],
                Metadata: metadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions:
                [
                    new RuntimeStateChange<ActivityExecutionState>(
                        StateId: request.ActivityExecutionState.Execution.ActivityExecutionId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: request.ActivityExecutionState,
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
                // The resume callback's workflow-scope variable write-back (#286/#310) commits in the same
                // transactional unit as the bookmark-consumed checkpoint, so a variable mutated inside a resume
                // callback is durably re-projected for downstream activities iff the bookmark is consumed.
                durableValues: request.DurableValueChanges,
                incidents: [],
                operational: [],
                activityExecutionInspections: inspection is null
                    ? []
                    :
                    [
                        new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                            StateId: request.ActivityExecutionState.Execution.ActivityExecutionId,
                            Operation: RuntimeStateChangeOperation.Upsert,
                            State: inspection,
                            Metadata: metadata)
                    ]),
            PostCommitIntents: request.ContinuationWorkItems
                .Select(workItem => SchedulerWorkHandlerHelpers.NewEnqueueSchedulerWorkIntent(
                    request.ResumeWorkItem,
                    request.ActivityExecutionState.Execution.ActivityExecutionId,
                    workItem,
                    occurredAt))
                .ToArray(),
            Metadata: metadata);

        var result = await _checkpointCommitter.CommitAsync(commit, cancellationToken);

        // Notify bookmark-lifecycle observers AFTER the durable commit deleted the bookmark (spec 089 D).
        // Observer failures are caught and logged inside the notifier — they never fault this resumed run.
        if (_bookmarkLifecycleNotifier is not null)
            await _bookmarkLifecycleNotifier.NotifyConsumedAsync(request.Bookmark, cancellationToken);

        return new BookmarkConsumptionCheckpointResult(commitId, checkpointId, result);
    }

    private static IReadOnlyCollection<string> ReadOutcomeNames(ActivityExecutionState state)
    {
        if (state.Status != ActivityExecutionStatus.Completed)
            return [];

        if (state.Metadata.TryGetValue(RuntimeMetadataKeys.CompletionOutcomeNames, out var serializedOutcomeNames))
        {
            var outcomeNames = JsonSerializer.Deserialize<string[]>(serializedOutcomeNames)
                ?? throw new InvalidOperationException("Persisted completion outcome names resolved to null.");

            return outcomeNames;
        }

        return [ActivityOutcomes.Done];
    }
}
