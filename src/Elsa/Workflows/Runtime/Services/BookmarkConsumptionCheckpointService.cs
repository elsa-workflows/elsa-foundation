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

        var checkpointSlug = request.CheckpointName.ToLowerInvariant();
        var discriminator = request.CommitDiscriminator is null ? "" : $":{request.CommitDiscriminator}";
        var checkpointId = $"checkpoint:{request.ResumeWorkItem.WorkItemId}:{checkpointSlug}:{request.ResumePayload.BookmarkId}{discriminator}";
        var commitId = $"commit:{request.ResumeWorkItem.WorkItemId}:{checkpointSlug}:{request.ResumePayload.BookmarkId}{discriminator}";
        var occurredAt = _timeProvider.GetUtcNow();
        var commitMetadata = new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.SchedulerWorkItemId] = request.ResumeWorkItem.WorkItemId,
            [RuntimeMetadataKeys.CommandId] = request.ResumeWorkItem.CommandId,
            [RuntimeMetadataKeys.CheckpointReason] = request.ResumePayload.Reason,
            [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory,
            [RuntimeMetadataKeys.BookmarkId] = request.ResumePayload.BookmarkId,
            [RuntimeMetadataKeys.ActivityExecutionId] = request.ActivityExecutionState.Execution.ActivityExecutionId,
            [RuntimeMetadataKeys.ExecutableNodeId] = request.ResumePayload.ExecutableNodeId,
            [RuntimeMetadataKeys.ResumeTargetId] = request.ResumePayload.ResumeTargetId,
            [RuntimeMetadataKeys.StimulusType] = request.ResumePayload.StimulusType,
            [RuntimeMetadataKeys.StimulusHash] = request.ResumePayload.StimulusHash,
            [RuntimeMetadataKeys.ExecutableArtifactId] = request.ResumePayload.PinnedExecutable.ArtifactId,
            [RuntimeMetadataKeys.ExecutableArtifactVersion] = request.ResumePayload.PinnedExecutable.ArtifactVersion,
            [RuntimeMetadataKeys.ExecutableArtifactHash] = request.ResumePayload.PinnedExecutable.ArtifactHash
        };
        if (request.ActivityExecutionState.Metadata.TryGetValue(RuntimeMetadataKeys.ActivityAttemptActivationClaim, out var attemptId))
            commitMetadata[RuntimeMetadataKeys.ActivityAttemptActivationClaim] = attemptId;
        if (request.ActivityExecutionState.Metadata.TryGetValue(RuntimeMetadataKeys.ActivityAttemptActivationClaimWorkItemId, out var claimWorkItemId))
            commitMetadata[RuntimeMetadataKeys.ActivityAttemptActivationClaimWorkItemId] = claimWorkItemId;
        var metadata = RuntimeModelMetadata.Snapshot(commitMetadata);
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
                Name: request.CheckpointName,
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
                bookmarks: !request.ConsumesBookmarks
                    ? []
                    : request.BookmarksToConsume
                        .Select(bookmark => new RuntimeStateChange<BookmarkState>(
                            StateId: bookmark.BookmarkId,
                            Operation: RuntimeStateChangeOperation.Delete,
                            State: bookmark,
                            Metadata: metadata))
                        .ToArray(),
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
        // Observer failures are caught and logged inside the notifier — they never fault this resumed run. Once
        // the commit lands, finish the whole notification batch even if the scheduler delivery token is cancelled.
        if (request.ConsumesBookmarks && _bookmarkLifecycleNotifier is not null)
        {
            foreach (var consumedBookmark in request.BookmarksToConsume)
                await _bookmarkLifecycleNotifier.NotifyConsumedAsync(consumedBookmark, CancellationToken.None);
        }

        return new BookmarkConsumptionCheckpointResult(commitId, checkpointId, request.CheckpointName, result);
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
