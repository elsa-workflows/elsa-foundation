using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class BookmarkConsumptionCheckpointService : IBookmarkConsumptionCheckpointService
{
    private readonly RuntimeCheckpointCommitter _checkpointCommitter;
    private readonly TimeProvider _timeProvider;

    public BookmarkConsumptionCheckpointService(RuntimeCheckpointCommitter checkpointCommitter)
        : this(checkpointCommitter, TimeProvider.System)
    {
    }

    public BookmarkConsumptionCheckpointService(
        RuntimeCheckpointCommitter checkpointCommitter,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(checkpointCommitter);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _checkpointCommitter = checkpointCommitter;
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
            ["runtime.schedulerWorkItemId"] = request.ResumeWorkItem.WorkItemId,
            ["runtime.commandId"] = request.ResumeWorkItem.CommandId,
            ["runtime.checkpointReason"] = request.ResumePayload.Reason,
            ["runtime.bookmarkId"] = request.Bookmark.BookmarkId,
            ["runtime.activityExecutionId"] = request.CompletedActivityExecutionState.Execution.ActivityExecutionId,
            ["runtime.executableNodeId"] = request.ResumePayload.ExecutableNodeId,
            ["runtime.resumeTargetId"] = request.ResumePayload.ResumeTargetId,
            ["runtime.stimulusType"] = request.ResumePayload.StimulusType,
            ["runtime.stimulusHash"] = request.ResumePayload.StimulusHash,
            ["runtime.executableArtifactId"] = request.ResumePayload.PinnedExecutable.ArtifactId,
            ["runtime.executableArtifactVersion"] = request.ResumePayload.PinnedExecutable.ArtifactVersion,
            ["runtime.executableArtifactHash"] = request.ResumePayload.PinnedExecutable.ArtifactHash
        });
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
                operational: []),
            PostCommitIntents: [],
            Metadata: metadata);

        var decision = await _checkpointCommitter.CommitAsync(commit, cancellationToken);
        return new BookmarkConsumptionCheckpointResult(commitId, checkpointId, decision);
    }
}
