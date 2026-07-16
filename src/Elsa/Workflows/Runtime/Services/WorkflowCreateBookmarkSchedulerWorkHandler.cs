using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class WorkflowCreateBookmarkSchedulerWorkHandler : IWorkflowSchedulerWorkHandler, IRuntimePipelineWorkHandler
{
    public const string HandlerName = nameof(WorkflowCreateBookmarkSchedulerWorkHandler);
    private const string SuspendedSubStatus = "BookmarkWaiting";

    private readonly IWorkflowExecutableStore _workflowExecutableStore;
    private readonly IActivityExecutionStateStore _activityExecutionStateStore;
    private readonly RuntimeCheckpointCommitter _checkpointCommitter;
    private readonly IRuntimeActivityExecutionInspectionAccumulator? _inspectionAccumulator;
    private readonly BookmarkLifecycleNotifier? _bookmarkLifecycleNotifier;
    private readonly TimeProvider _timeProvider;

    public WorkflowCreateBookmarkSchedulerWorkHandler(
        IWorkflowExecutableStore workflowExecutableStore,
        IActivityExecutionStateStore activityExecutionStateStore,
        RuntimeCheckpointCommitter checkpointCommitter,
        IRuntimeActivityExecutionInspectionAccumulator? inspectionAccumulator,
        TimeProvider timeProvider,
        BookmarkLifecycleNotifier? bookmarkLifecycleNotifier = null)
    {
        ArgumentNullException.ThrowIfNull(workflowExecutableStore);
        ArgumentNullException.ThrowIfNull(activityExecutionStateStore);
        ArgumentNullException.ThrowIfNull(checkpointCommitter);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _workflowExecutableStore = workflowExecutableStore;
        _activityExecutionStateStore = activityExecutionStateStore;
        _checkpointCommitter = checkpointCommitter;
        _inspectionAccumulator = inspectionAccumulator;
        _bookmarkLifecycleNotifier = bookmarkLifecycleNotifier;
        _timeProvider = timeProvider;
    }

    public string Name => HandlerName;

    public bool CanHandle(RuntimeSchedulerWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        return workItem.CommandKind == WorkflowExecutionCommandKind.CreateBookmark;
    }

    /// <summary>Direct (no-pipeline) dispatch: build the bookmark-created commit and commit it inline.</summary>
    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        var commit = await ExecuteAsync(workItem, cancellationToken);
        if (commit is null)
            return;

        await _checkpointCommitter.CommitAsync(commit, cancellationToken);

        // Notify bookmark-lifecycle observers AFTER the durable commit (spec 089 D). Observer failures are caught
        // and logged inside the notifier — they never fault this run. The created bookmark rides the commit's
        // upsert state change, so we read it back rather than re-deriving it.
        await NotifyBookmarksCreatedAsync(commit, cancellationToken);
    }

    private async ValueTask NotifyBookmarksCreatedAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken)
    {
        if (_bookmarkLifecycleNotifier is null)
            return;

        foreach (var change in commit.StateChanges.Bookmarks)
        {
            if (change is { Operation: RuntimeStateChangeOperation.Upsert, State: { } bookmark })
                await _bookmarkLifecycleNotifier.NotifyCreatedAsync(bookmark, cancellationToken);
        }
    }

    /// <summary>Pipeline dispatch (Move 2): run the handler in the Invoke slot and stage its commit for the Checkpoint slot.</summary>
    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, IRuntimePipelineContext pipelineContext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pipelineContext);
        var commit = await ExecuteAsync(workItem, cancellationToken);
        if (commit is not null)
            pipelineContext.Workspace.StageCheckpointCommit(commit);
    }

    private async ValueTask<RuntimeCheckpointCommit?> ExecuteAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        var payload = DeserializePayload(workItem);
        var executable = await _workflowExecutableStore.FindAsync(payload.PinnedExecutable.ArtifactId, cancellationToken);
        if (executable is null)
            throw new WorkflowExecutableNotFoundException(payload.PinnedExecutable.ArtifactId);

        SchedulerWorkHandlerHelpers.ValidatePinnedExecutable(workItem, payload.PinnedExecutable, executable.Identity);

        if (!executable.NodesById.ContainsKey(payload.ExecutableNodeId))
            throw new InvalidOperationException($"CreateBookmark scheduler work item '{workItem.WorkItemId}' references executable node '{payload.ExecutableNodeId}', which is missing from executable artifact '{WorkflowExecutableIdentityComparer.Format(executable.Identity)}'.");

        var requestedResumeTargetId = payload.ResumeTargetId;
        var resumeTarget = ResolveResumeTarget(executable, payload.ExecutableNodeId, requestedResumeTargetId, workItem.WorkItemId);
        if (!StringComparer.Ordinal.Equals(payload.ResumeTargetId, resumeTarget.ResumeTargetId))
            payload = WithResumeTargetId(payload, resumeTarget.ResumeTargetId);

        if (!StringComparer.Ordinal.Equals(resumeTarget.ExecutableNodeId, payload.ExecutableNodeId))
            throw new InvalidOperationException($"CreateBookmark scheduler work item '{workItem.WorkItemId}' references executable node '{payload.ExecutableNodeId}', but resume target '{payload.ResumeTargetId}' points at executable node '{resumeTarget.ExecutableNodeId}'.");

        var state = await _activityExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, payload.ActivityExecutionId, cancellationToken);
        if (state is null)
            throw new InvalidOperationException($"CreateBookmark scheduler work item '{workItem.WorkItemId}' references missing activity execution '{payload.ActivityExecutionId}' for workflow execution '{workItem.WorkflowExecutionId}'.");

        if (!StringComparer.Ordinal.Equals(state.Execution.ExecutableNodeId, payload.ExecutableNodeId))
            throw new InvalidOperationException($"CreateBookmark scheduler work item '{workItem.WorkItemId}' references executable node '{payload.ExecutableNodeId}', but activity execution '{payload.ActivityExecutionId}' belongs to executable node '{state.Execution.ExecutableNodeId}'.");

        if (state.Status is ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled or ActivityExecutionStatus.Recovered)
            return null;

        if (state.Status is not ActivityExecutionStatus.Running and not ActivityExecutionStatus.Suspended)
            throw new InvalidOperationException($"CreateBookmark scheduler work item '{workItem.WorkItemId}' cannot create a durable bookmark for activity execution '{payload.ActivityExecutionId}' while it is '{state.Status}'.");

        var bookmark = NewBookmark(workItem, payload);
        var suspendedState = SuspendActivity(workItem, payload, state);
        return await NewCommitAsync(workItem, payload, suspendedState, bookmark, cancellationToken);
    }

    private static WorkflowExecutableResumeTarget ResolveResumeTarget(
        WorkflowExecutable executable,
        string executableNodeId,
        string requestedResumeTargetId,
        string workItemId)
    {
        if (executable.ResumeTargets.TryGetValue(requestedResumeTargetId, out var direct))
            return direct;

        var matches = executable.ResumeTargets.Values
            .Where(x => StringComparer.Ordinal.Equals(x.ExecutableNodeId, executableNodeId))
            .Where(x => StringComparer.Ordinal.Equals(x.LocalResumeTargetId, requestedResumeTargetId))
            .ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"CreateBookmark scheduler work item '{workItemId}' references resume target '{requestedResumeTargetId}', which is missing from executable artifact '{WorkflowExecutableIdentityComparer.Format(executable.Identity)}'."),
            _ => throw new InvalidOperationException($"CreateBookmark scheduler work item '{workItemId}' references local resume target '{requestedResumeTargetId}' ambiguously for executable node '{executableNodeId}'.")
        };
    }

    private static RuntimeCreateBookmarkCommandPayload WithResumeTargetId(
        RuntimeCreateBookmarkCommandPayload payload,
        string resumeTargetId) => new(
        payload.PinnedExecutable,
        payload.BookmarkId,
        payload.ActivityExecutionId,
        payload.ExecutableNodeId,
        resumeTargetId,
        payload.StimulusType,
        payload.StimulusHash,
        payload.Payload,
        payload.ExpiresAt,
        payload.Reason,
        payload.Metadata,
        payload.ValueSnapshots,
        payload.DurableValueChanges);

    private async ValueTask<RuntimeCheckpointCommit> NewCommitAsync(
        RuntimeSchedulerWorkItem workItem,
        RuntimeCreateBookmarkCommandPayload payload,
        ActivityExecutionState suspendedState,
        BookmarkState bookmark,
        CancellationToken cancellationToken)
    {
        var occurredAt = _timeProvider.GetUtcNow();
        var checkpointId = $"checkpoint:{workItem.WorkItemId}:bookmark-created:{payload.BookmarkId}";
        var metadata = RuntimeModelMetadata.Snapshot(new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId,
            [RuntimeMetadataKeys.CommandId] = workItem.CommandId,
            [RuntimeMetadataKeys.CheckpointReason] = payload.Reason,
            [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory,
            [RuntimeMetadataKeys.BookmarkId] = payload.BookmarkId,
            [RuntimeMetadataKeys.ActivityExecutionId] = payload.ActivityExecutionId,
            [RuntimeMetadataKeys.ExecutableNodeId] = payload.ExecutableNodeId,
            [RuntimeMetadataKeys.ResumeTargetId] = payload.ResumeTargetId,
            [RuntimeMetadataKeys.StimulusType] = payload.StimulusType,
            [RuntimeMetadataKeys.StimulusHash] = payload.StimulusHash,
            [RuntimeMetadataKeys.ExecutableArtifactId] = payload.PinnedExecutable.ArtifactId,
            [RuntimeMetadataKeys.ExecutableArtifactVersion] = payload.PinnedExecutable.ArtifactVersion,
            [RuntimeMetadataKeys.ExecutableArtifactHash] = payload.PinnedExecutable.ArtifactHash
        });
        var inspection = _inspectionAccumulator is null
            ? null
            : await _inspectionAccumulator.BuildProjectionAsync(
                suspendedState,
                checkpointId,
                occurredAt,
                bookmarks: [ActivityExecutionBookmarkSummary.From(bookmark)],
                valueSnapshots: payload.ValueSnapshots,
                metadata: metadata,
                cancellationToken: cancellationToken);

        return new RuntimeCheckpointCommit(
            CommitId: $"commit:{workItem.WorkItemId}:bookmark-created:{payload.BookmarkId}",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: checkpointId,
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
                // Commit any suspend-path write-back the invoke handler folded into this bookmark's payload
                // (#310) in the same transactional unit as the bookmark, so the workflow-scope variable
                // write-back / SetOutput durable values land atomically with the bookmark-created checkpoint.
                durableValues: payload.DurableValueChanges,
                incidents: [],
                operational: [],
                activityExecutionInspections: inspection is null
                    ? []
                    :
                    [
                        new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                            StateId: payload.ActivityExecutionId,
                            Operation: RuntimeStateChangeOperation.Upsert,
                            State: inspection,
                            Metadata: metadata)
                    ]),
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
        metadata[RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId;
        metadata[RuntimeMetadataKeys.CommandId] = workItem.CommandId;
        metadata[RuntimeMetadataKeys.Reason] = payload.Reason;
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
        metadata[RuntimeMetadataKeys.BookmarkId] = payload.BookmarkId;
        metadata[RuntimeMetadataKeys.ResumeTargetId] = payload.ResumeTargetId;
        metadata[RuntimeMetadataKeys.SuspendReason] = payload.Reason;
        metadata[RuntimeMetadataKeys.CreateBookmarkSchedulerWorkItemId] = workItem.WorkItemId;

        return state with
        {
            Status = ActivityExecutionStatus.Suspended,
            SubStatus = SuspendedSubStatus,
            BookmarkIds = bookmarkIds,
            Metadata = metadata
        };
    }

    private static RuntimeCreateBookmarkCommandPayload DeserializePayload(RuntimeSchedulerWorkItem workItem) =>
        SchedulerWorkHandlerHelpers.DeserializePayload(
            workItem,
            requiresPayloadMessage: "CreateBookmark scheduler work item requires a create bookmark payload.",
            resolvedToNullMessage: "CreateBookmark scheduler work item payload resolved to null.",
            invalidPayloadMessage: "CreateBookmark scheduler work item payload is not a valid create bookmark payload.",
            deserialize: static (_, payload) => payload.Deserialize<RuntimeCreateBookmarkCommandPayload>(),
            isPayloadValidationException: static exception =>
                exception is JsonException or NotSupportedException or RuntimeCreateBookmarkCommandPayloadValidationException);
}
