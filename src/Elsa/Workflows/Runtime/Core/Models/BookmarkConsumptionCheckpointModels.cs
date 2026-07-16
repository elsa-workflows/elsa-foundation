using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;

namespace Elsa.Workflows.Runtime.Core.Models;

public sealed class BookmarkConsumptionCheckpointRequest
{
    public BookmarkConsumptionCheckpointRequest(
        RuntimeSchedulerWorkItem resumeWorkItem,
        RuntimeResumeBookmarkCommandPayload resumePayload,
        BookmarkState bookmark,
        ActivityExecutionState activityExecutionState,
        RuntimeSchedulerWorkItem? completionWorkItem = null,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot>? valueSnapshots = null,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>>? durableValueChanges = null,
        IReadOnlyCollection<RuntimeSchedulerWorkItem>? continuationWorkItems = null)
    {
        ArgumentNullException.ThrowIfNull(resumeWorkItem);
        ArgumentNullException.ThrowIfNull(resumePayload);
        ArgumentNullException.ThrowIfNull(bookmark);
        ArgumentNullException.ThrowIfNull(activityExecutionState);

        if (resumeWorkItem.CommandKind != WorkflowExecutionCommandKind.ResumeBookmark)
            throw new ArgumentException("Bookmark consumption checkpoints require ResumeBookmark scheduler work.", nameof(resumeWorkItem));

        if (activityExecutionState.Status is not ActivityExecutionStatus.Completed and not ActivityExecutionStatus.Suspended)
            throw new ArgumentException("Bookmark consumption checkpoints require completed or replacement-suspended activity execution state.", nameof(activityExecutionState));

        ValidateBookmarkMatchesResumePayload(resumeWorkItem.WorkflowExecutionId, resumePayload, bookmark, nameof(bookmark));
        ValidateActivityStateMatchesResumePayload(resumeWorkItem.WorkflowExecutionId, resumePayload, activityExecutionState, nameof(activityExecutionState));
        if (completionWorkItem is not null)
            ValidateCompletionWorkItem(resumeWorkItem.WorkflowExecutionId, activityExecutionState.Execution.ActivityExecutionId, completionWorkItem, nameof(completionWorkItem));

        var continuations = continuationWorkItems?.ToArray() ?? (completionWorkItem is null ? [] : [completionWorkItem]);
        if (completionWorkItem is not null && continuationWorkItems is not null)
            throw new ArgumentException("Specify either the completion work item or the continuation work item collection, not both.", nameof(continuationWorkItems));
        if (activityExecutionState.Status == ActivityExecutionStatus.Completed && continuations.Any(item => item.CommandKind != WorkflowExecutionCommandKind.CompleteActivity))
            throw new ArgumentException("Completed bookmark consumption can only schedule completion continuation work.", nameof(continuationWorkItems));
        if (activityExecutionState.Status == ActivityExecutionStatus.Suspended && continuations.Any(item => item.CommandKind != WorkflowExecutionCommandKind.CreateBookmark))
            throw new ArgumentException("Replacement suspension can only schedule bookmark-creation continuation work.", nameof(continuationWorkItems));

        ResumeWorkItem = resumeWorkItem;
        ResumePayload = resumePayload;
        Bookmark = bookmark;
        ActivityExecutionState = activityExecutionState;
        ContinuationWorkItems = continuations;
        ValueSnapshots = valueSnapshots ?? [];
        DurableValueChanges = durableValueChanges ?? [];
    }

    public RuntimeSchedulerWorkItem ResumeWorkItem { get; }
    public RuntimeResumeBookmarkCommandPayload ResumePayload { get; }
    public BookmarkState Bookmark { get; }
    public ActivityExecutionState ActivityExecutionState { get; }
    public IReadOnlyCollection<RuntimeSchedulerWorkItem> ContinuationWorkItems { get; }
    public IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> ValueSnapshots { get; }

    /// <summary>
    /// Durable-value changes (e.g. the resume callback's workflow-scope variable write-back, #286/#310) to commit
    /// atomically with the bookmark-consumed checkpoint. Mirrors how the invoke path folds
    /// <c>RuntimeContainerScopeService.BuildWorkflowScopeWriteBackChanges</c> (engine package) output into its completion
    /// commit, so a variable a resume callback mutated is durably re-projected for downstream activities rather
    /// than being lost when the in-memory scope is discarded. Empty unless the resume callback mutated a variable.
    /// </summary>
    public IReadOnlyCollection<RuntimeStateChange<DurableValueState>> DurableValueChanges { get; }

    private static void ValidateBookmarkMatchesResumePayload(
        string workflowExecutionId,
        RuntimeResumeBookmarkCommandPayload payload,
        BookmarkState bookmark,
        string parameterName)
    {
        if (!StringComparer.Ordinal.Equals(bookmark.WorkflowExecutionId, workflowExecutionId))
            throw new ArgumentException("Bookmark workflow execution ID must match ResumeBookmark scheduler work.", parameterName);

        if (!StringComparer.Ordinal.Equals(bookmark.BookmarkId, payload.BookmarkId))
            throw new ArgumentException("Bookmark ID must match ResumeBookmark payload.", parameterName);

        if (!StringComparer.Ordinal.Equals(bookmark.ActivityExecutionId, payload.ActivityExecutionId))
            throw new ArgumentException("Bookmark activity execution ID must match ResumeBookmark payload.", parameterName);

        if (!StringComparer.Ordinal.Equals(bookmark.ExecutableNodeId, payload.ExecutableNodeId))
            throw new ArgumentException("Bookmark executable node ID must match ResumeBookmark payload.", parameterName);

        if (!StringComparer.Ordinal.Equals(bookmark.ResumeTargetId, payload.ResumeTargetId))
            throw new ArgumentException("Bookmark resume target ID must match ResumeBookmark payload.", parameterName);

        if (!StringComparer.Ordinal.Equals(bookmark.StimulusType, payload.StimulusType))
            throw new ArgumentException("Bookmark stimulus type must match ResumeBookmark payload.", parameterName);

        if (!StringComparer.Ordinal.Equals(bookmark.StimulusHash, payload.StimulusHash))
            throw new ArgumentException("Bookmark stimulus hash must match ResumeBookmark payload.", parameterName);
    }

    private static void ValidateActivityStateMatchesResumePayload(
        string workflowExecutionId,
        RuntimeResumeBookmarkCommandPayload payload,
        ActivityExecutionState state,
        string parameterName)
    {
        if (!StringComparer.Ordinal.Equals(state.Execution.WorkflowExecutionId, workflowExecutionId))
            throw new ArgumentException("Activity execution state workflow execution ID must match ResumeBookmark scheduler work.", parameterName);

        if (!StringComparer.Ordinal.Equals(state.Execution.ActivityExecutionId, payload.ActivityExecutionId))
            throw new ArgumentException("Activity execution state ID must match ResumeBookmark payload.", parameterName);

        if (!StringComparer.Ordinal.Equals(state.Execution.ExecutableNodeId, payload.ExecutableNodeId))
            throw new ArgumentException("Activity execution state executable node ID must match ResumeBookmark payload.", parameterName);
    }

    private static void ValidateCompletionWorkItem(
        string workflowExecutionId,
        string activityExecutionId,
        RuntimeSchedulerWorkItem workItem,
        string parameterName)
    {
        if (workItem.CommandKind != WorkflowExecutionCommandKind.CompleteActivity)
            throw new ArgumentException("Bookmark consumption completion work must be CompleteActivity scheduler work.", parameterName);

        if (!StringComparer.Ordinal.Equals(workItem.WorkflowExecutionId, workflowExecutionId))
            throw new ArgumentException("Bookmark consumption completion work workflow execution ID must match ResumeBookmark scheduler work.", parameterName);

        if (workItem.Payload is not { } payload)
            throw new ArgumentException("Bookmark consumption completion work requires a complete activity payload.", parameterName);

        var completePayload = payload.Deserialize<RuntimeCompleteActivityCommandPayload>()
                              ?? throw new ArgumentException("Bookmark consumption completion work payload resolved to null.", parameterName);
        if (!StringComparer.Ordinal.Equals(completePayload.ActivityExecutionId, activityExecutionId))
            throw new ArgumentException("Bookmark consumption completion work activity execution ID must match completed state.", parameterName);
    }
}

public sealed record BookmarkConsumptionCheckpointResult(
    string CommitId,
    string CheckpointId,
    RuntimeCheckpointCommitResult CommitResult)
{
    public string CheckpointName => RuntimeCheckpointNames.BookmarkConsumed;
    public RuntimeCheckpointPersistenceDecision PersistenceDecision => CommitResult.PersistenceDecision;
}
