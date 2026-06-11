using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class WorkflowCreateBookmarkSchedulerWorkHandler : IWorkflowSchedulerWorkHandler
{
    public const string HandlerName = nameof(WorkflowCreateBookmarkSchedulerWorkHandler);
    private const string SuspendedSubStatus = "BookmarkWaiting";

    private readonly IWorkflowExecutableStore _workflowExecutableStore;
    private readonly IActivityExecutionStateStore _activityExecutionStateStore;
    private readonly RuntimeCheckpointCommitter _checkpointCommitter;
    private readonly TimeProvider _timeProvider;

    public WorkflowCreateBookmarkSchedulerWorkHandler(
        IWorkflowExecutableStore workflowExecutableStore,
        IActivityExecutionStateStore activityExecutionStateStore,
        RuntimeCheckpointCommitter checkpointCommitter)
        : this(workflowExecutableStore, activityExecutionStateStore, checkpointCommitter, TimeProvider.System)
    {
    }

    public WorkflowCreateBookmarkSchedulerWorkHandler(
        IWorkflowExecutableStore workflowExecutableStore,
        IActivityExecutionStateStore activityExecutionStateStore,
        RuntimeCheckpointCommitter checkpointCommitter,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(workflowExecutableStore);
        ArgumentNullException.ThrowIfNull(activityExecutionStateStore);
        ArgumentNullException.ThrowIfNull(checkpointCommitter);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _workflowExecutableStore = workflowExecutableStore;
        _activityExecutionStateStore = activityExecutionStateStore;
        _checkpointCommitter = checkpointCommitter;
        _timeProvider = timeProvider;
    }

    public string Name => HandlerName;

    public bool CanHandle(RuntimeSchedulerWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        return workItem.CommandKind == WorkflowExecutionCommandKind.CreateBookmark;
    }

    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        var payload = DeserializePayload(workItem);
        var executable = await _workflowExecutableStore.FindAsync(payload.PinnedExecutable.ArtifactId, cancellationToken);
        if (executable is null)
            throw new WorkflowExecutableNotFoundException(payload.PinnedExecutable.ArtifactId);

        ValidatePinnedExecutable(workItem, payload.PinnedExecutable, executable.Identity);

        if (!executable.NodesById.ContainsKey(payload.ExecutableNodeId))
            throw new InvalidOperationException($"CreateBookmark scheduler work item '{workItem.WorkItemId}' references executable node '{payload.ExecutableNodeId}', which is missing from executable artifact '{WorkflowExecutableIdentityComparer.Format(executable.Identity)}'.");

        if (!executable.ResumeTargets.TryGetValue(payload.ResumeTargetId, out var resumeTarget))
            throw new InvalidOperationException($"CreateBookmark scheduler work item '{workItem.WorkItemId}' references resume target '{payload.ResumeTargetId}', which is missing from executable artifact '{WorkflowExecutableIdentityComparer.Format(executable.Identity)}'.");

        if (!StringComparer.Ordinal.Equals(resumeTarget.ExecutableNodeId, payload.ExecutableNodeId))
            throw new InvalidOperationException($"CreateBookmark scheduler work item '{workItem.WorkItemId}' references executable node '{payload.ExecutableNodeId}', but resume target '{payload.ResumeTargetId}' points at executable node '{resumeTarget.ExecutableNodeId}'.");

        var state = await _activityExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, payload.ActivityExecutionId, cancellationToken);
        if (state is null)
            throw new InvalidOperationException($"CreateBookmark scheduler work item '{workItem.WorkItemId}' references missing activity execution '{payload.ActivityExecutionId}' for workflow execution '{workItem.WorkflowExecutionId}'.");

        if (!StringComparer.Ordinal.Equals(state.Execution.ExecutableNodeId, payload.ExecutableNodeId))
            throw new InvalidOperationException($"CreateBookmark scheduler work item '{workItem.WorkItemId}' references executable node '{payload.ExecutableNodeId}', but activity execution '{payload.ActivityExecutionId}' belongs to executable node '{state.Execution.ExecutableNodeId}'.");

        if (state.Status is ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled)
            return;

        if (state.Status is not ActivityExecutionStatus.Running and not ActivityExecutionStatus.Suspended)
            throw new InvalidOperationException($"CreateBookmark scheduler work item '{workItem.WorkItemId}' cannot create a durable bookmark for activity execution '{payload.ActivityExecutionId}' while it is '{state.Status}'.");

        var bookmark = NewBookmark(workItem, payload);
        var suspendedState = SuspendActivity(workItem, payload, state);
        var commit = NewCommit(workItem, payload, suspendedState, bookmark);
        await _checkpointCommitter.CommitAsync(commit, cancellationToken);
    }

    private RuntimeCheckpointCommit NewCommit(
        RuntimeSchedulerWorkItem workItem,
        RuntimeCreateBookmarkCommandPayload payload,
        ActivityExecutionState suspendedState,
        BookmarkState bookmark)
    {
        var occurredAt = _timeProvider.GetUtcNow();
        var metadata = RuntimeModelMetadata.Snapshot(new Dictionary<string, string>
        {
            ["runtime.schedulerWorkItemId"] = workItem.WorkItemId,
            ["runtime.commandId"] = workItem.CommandId,
            ["runtime.checkpointReason"] = payload.Reason,
            ["runtime.bookmarkId"] = payload.BookmarkId,
            ["runtime.activityExecutionId"] = payload.ActivityExecutionId,
            ["runtime.executableNodeId"] = payload.ExecutableNodeId,
            ["runtime.resumeTargetId"] = payload.ResumeTargetId,
            ["runtime.stimulusType"] = payload.StimulusType,
            ["runtime.stimulusHash"] = payload.StimulusHash,
            ["runtime.executableArtifactId"] = payload.PinnedExecutable.ArtifactId,
            ["runtime.executableArtifactVersion"] = payload.PinnedExecutable.ArtifactVersion,
            ["runtime.executableArtifactHash"] = payload.PinnedExecutable.ArtifactHash
        });

        return new RuntimeCheckpointCommit(
            CommitId: $"commit:{workItem.WorkItemId}:bookmark-created:{payload.BookmarkId}",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: $"checkpoint:{workItem.WorkItemId}:bookmark-created:{payload.BookmarkId}",
                Name: RuntimeCheckpointNames.BookmarkCreated,
                WorkflowExecutionId: workItem.WorkflowExecutionId,
                OccurredAt: occurredAt,
                ActivityExecutionIds: [payload.ActivityExecutionId],
                Metadata: metadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions:
                [
                    new RuntimeStateChange<ActivityExecutionState>(
                        StateId: payload.ActivityExecutionId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: suspendedState,
                        Metadata: metadata)
                ],
                bookmarks:
                [
                    new RuntimeStateChange<BookmarkState>(
                        StateId: payload.BookmarkId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: bookmark,
                        Metadata: metadata)
                ],
                durableValues: [],
                incidents: [],
                operational: []),
            PostCommitIntents: [],
            Metadata: metadata);
    }

    private BookmarkState NewBookmark(RuntimeSchedulerWorkItem workItem, RuntimeCreateBookmarkCommandPayload payload) =>
        new(
            BookmarkId: payload.BookmarkId,
            WorkflowExecutionId: workItem.WorkflowExecutionId,
            ActivityExecutionId: payload.ActivityExecutionId,
            ExecutableNodeId: payload.ExecutableNodeId,
            ResumeTargetId: payload.ResumeTargetId,
            StimulusType: payload.StimulusType,
            StimulusHash: payload.StimulusHash,
            Payload: payload.Payload,
            Metadata: MergeBookmarkMetadata(workItem, payload),
            CreatedAt: _timeProvider.GetUtcNow(),
            ExpiresAt: payload.ExpiresAt);

    private static IReadOnlyDictionary<string, string> MergeBookmarkMetadata(
        RuntimeSchedulerWorkItem workItem,
        RuntimeCreateBookmarkCommandPayload payload)
    {
        var metadata = payload.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata["runtime.schedulerWorkItemId"] = workItem.WorkItemId;
        metadata["runtime.commandId"] = workItem.CommandId;
        metadata["runtime.reason"] = payload.Reason;
        return RuntimeModelMetadata.Snapshot(metadata);
    }

    private ActivityExecutionState SuspendActivity(
        RuntimeSchedulerWorkItem workItem,
        RuntimeCreateBookmarkCommandPayload payload,
        ActivityExecutionState state)
    {
        var bookmarkIds = state.BookmarkIds
            .Append(payload.BookmarkId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var metadata = state.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata["runtime.bookmarkId"] = payload.BookmarkId;
        metadata["runtime.resumeTargetId"] = payload.ResumeTargetId;
        metadata["runtime.suspendReason"] = payload.Reason;
        metadata["runtime.createBookmarkSchedulerWorkItemId"] = workItem.WorkItemId;

        return state with
        {
            Status = ActivityExecutionStatus.Suspended,
            SubStatus = SuspendedSubStatus,
            BookmarkIds = bookmarkIds,
            Metadata = metadata
        };
    }

    private static RuntimeCreateBookmarkCommandPayload DeserializePayload(RuntimeSchedulerWorkItem workItem)
    {
        if (workItem.Payload is not { } payload)
            throw new InvalidOperationException("CreateBookmark scheduler work item requires a create bookmark payload.");

        try
        {
            return payload.Deserialize<RuntimeCreateBookmarkCommandPayload>()
                   ?? throw new InvalidOperationException("CreateBookmark scheduler work item payload resolved to null.");
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException ||
            exception is RuntimeCreateBookmarkCommandPayloadValidationException)
        {
            throw new InvalidOperationException("CreateBookmark scheduler work item payload is not a valid create bookmark payload.", exception);
        }
    }

    private static void ValidatePinnedExecutable(
        RuntimeSchedulerWorkItem workItem,
        WorkflowExecutableIdentity pinnedExecutable,
        WorkflowExecutableIdentity loadedExecutable)
    {
        if (WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(loadedExecutable, pinnedExecutable))
            return;

        throw new InvalidOperationException(
            $"CreateBookmark scheduler work item '{workItem.WorkItemId}' loaded executable artifact '{WorkflowExecutableIdentityComparer.Format(loadedExecutable)}' " +
            $"but pinned executable artifact '{WorkflowExecutableIdentityComparer.Format(pinnedExecutable)}'.");
    }
}
