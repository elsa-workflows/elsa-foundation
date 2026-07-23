using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;

namespace Elsa.Workflows.Runtime.Core.Models;

public sealed class BookmarkConsumptionCheckpointRequest
{
    private BookmarkConsumptionCheckpointRequest(
        BookmarkResumeCheckpointTransitionKind transitionKind,
        RuntimeSchedulerWorkItem resumeWorkItem,
        RuntimeResumeBookmarkCommandPayload resumePayload,
        BookmarkState? resumeBookmark,
        IReadOnlyCollection<BookmarkState>? additionalBookmarksToConsume,
        ActivityExecutionState activityExecutionState,
        IReadOnlyCollection<RuntimeSchedulerWorkItem> continuationWorkItems,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> durableValueChanges,
        string? commitDiscriminator,
        RuntimeStateChange<WorkflowExecutionState>? workflowExecutionChange = null)
    {
        if (workflowExecutionChange is not null && transitionKind != BookmarkResumeCheckpointTransitionKind.ActivityCompletion)
            throw new ArgumentException($"Bookmark resume transition '{transitionKind}' cannot carry a workflow execution state change.", nameof(workflowExecutionChange));
        ArgumentNullException.ThrowIfNull(resumeWorkItem);
        ArgumentNullException.ThrowIfNull(resumePayload);
        ArgumentNullException.ThrowIfNull(activityExecutionState);
        ArgumentNullException.ThrowIfNull(continuationWorkItems);
        ArgumentNullException.ThrowIfNull(valueSnapshots);
        ArgumentNullException.ThrowIfNull(durableValueChanges);

        if (resumeWorkItem.CommandKind != WorkflowExecutionCommandKind.ResumeBookmark)
            throw new ArgumentException("Bookmark consumption checkpoints require ResumeBookmark scheduler work.", nameof(resumeWorkItem));

        var expectedStatus = transitionKind switch
        {
            BookmarkResumeCheckpointTransitionKind.InitialTriggerClaim or BookmarkResumeCheckpointTransitionKind.RedeliveryClaim => ActivityExecutionStatus.Running,
            BookmarkResumeCheckpointTransitionKind.StaleBookmarkConsumption or BookmarkResumeCheckpointTransitionKind.ActivityCompletion => ActivityExecutionStatus.Completed,
            BookmarkResumeCheckpointTransitionKind.ActivitySuspension => ActivityExecutionStatus.Suspended,
            _ => throw new ArgumentOutOfRangeException(nameof(transitionKind), transitionKind, "Unknown bookmark resume checkpoint transition.")
        };
        if (activityExecutionState.Status != expectedStatus)
            throw new ArgumentException($"Bookmark resume transition '{transitionKind}' requires activity status '{expectedStatus}'.", nameof(activityExecutionState));

        var consumesBookmarks = transitionKind is BookmarkResumeCheckpointTransitionKind.InitialTriggerClaim or BookmarkResumeCheckpointTransitionKind.StaleBookmarkConsumption;
        if (consumesBookmarks && resumeBookmark is null)
            throw new ArgumentNullException(nameof(resumeBookmark), $"Bookmark resume transition '{transitionKind}' requires the selected bookmark.");
        if (!consumesBookmarks && (resumeBookmark is not null || additionalBookmarksToConsume is { Count: > 0 }))
            throw new ArgumentException($"Bookmark resume transition '{transitionKind}' cannot consume bookmarks.", nameof(additionalBookmarksToConsume));
        if (resumeBookmark is not null)
            ValidateBookmarkMatchesResumePayload(resumeWorkItem.WorkflowExecutionId, resumePayload, resumeBookmark, nameof(resumeBookmark));
        var bookmarksToConsume = resumeBookmark is null
            ? []
            : new[] { resumeBookmark }
                .Concat(additionalBookmarksToConsume ?? [])
                .DistinctBy(candidate => candidate.BookmarkId, StringComparer.Ordinal)
                .ToArray();
        foreach (var candidate in bookmarksToConsume)
            ValidateOwnedBookmark(resumeWorkItem.WorkflowExecutionId, activityExecutionState, candidate, nameof(additionalBookmarksToConsume));
        ValidateActivityStateMatchesResumePayload(resumeWorkItem.WorkflowExecutionId, resumePayload, activityExecutionState, nameof(activityExecutionState));

        var continuations = continuationWorkItems.ToArray();
        if (transitionKind == BookmarkResumeCheckpointTransitionKind.ActivitySuspension)
        {
            if (continuations.Length == 0 || continuations.Any(item => item.CommandKind != WorkflowExecutionCommandKind.CreateBookmark))
                throw new ArgumentException("Activity suspension requires at least one bookmark-creation continuation.", nameof(continuationWorkItems));
        }
        else if (transitionKind is BookmarkResumeCheckpointTransitionKind.ActivityCompletion or BookmarkResumeCheckpointTransitionKind.StaleBookmarkConsumption)
        {
            if (continuations.Length != 1)
                throw new ArgumentException($"Bookmark resume transition '{transitionKind}' requires exactly one completion continuation.", nameof(continuationWorkItems));
            ValidateCompletionWorkItem(resumeWorkItem.WorkflowExecutionId, activityExecutionState.Execution.ActivityExecutionId, continuations[0], nameof(continuationWorkItems));
        }
        else if (continuations.Length != 0)
            throw new ArgumentException($"Bookmark resume transition '{transitionKind}' cannot schedule continuations.", nameof(continuationWorkItems));

        var isClaim = transitionKind is BookmarkResumeCheckpointTransitionKind.InitialTriggerClaim or BookmarkResumeCheckpointTransitionKind.RedeliveryClaim;
        if (isClaim)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(commitDiscriminator);
            var claimedAttempt = activityExecutionState.Attempts?
                .SingleOrDefault(attempt => attempt.EndedAt is null && StringComparer.Ordinal.Equals(attempt.AttemptId, commitDiscriminator));
            if (claimedAttempt is null ||
                !activityExecutionState.Metadata.TryGetValue(RuntimeMetadataKeys.ActivityAttemptActivationClaim, out var claimedAttemptId) ||
                !StringComparer.Ordinal.Equals(claimedAttemptId, commitDiscriminator) ||
                !activityExecutionState.Metadata.TryGetValue(RuntimeMetadataKeys.ActivityAttemptActivationClaimWorkItemId, out var claimedWorkItemId) ||
                !StringComparer.Ordinal.Equals(claimedWorkItemId, resumeWorkItem.WorkItemId))
            {
                throw new ArgumentException("An attempt-claim transition requires one matching open attempt and activation-claim metadata.", nameof(commitDiscriminator));
            }
        }
        else if (commitDiscriminator is not null)
            throw new ArgumentException($"Bookmark resume transition '{transitionKind}' cannot carry an attempt discriminator.", nameof(commitDiscriminator));

        TransitionKind = transitionKind;
        ResumeWorkItem = resumeWorkItem;
        ResumePayload = resumePayload;
        BookmarksToConsume = bookmarksToConsume;
        CommitDiscriminator = commitDiscriminator;
        ActivityExecutionState = activityExecutionState;
        ContinuationWorkItems = continuations;
        ValueSnapshots = valueSnapshots;
        DurableValueChanges = durableValueChanges;
        WorkflowExecutionChange = workflowExecutionChange;
    }

    public BookmarkResumeCheckpointTransitionKind TransitionKind { get; }
    public RuntimeSchedulerWorkItem ResumeWorkItem { get; }
    public RuntimeResumeBookmarkCommandPayload ResumePayload { get; }
    public IReadOnlyCollection<BookmarkState> BookmarksToConsume { get; }
    public bool ConsumesBookmarks => BookmarksToConsume.Count > 0;
    public string CheckpointName => TransitionKind switch
    {
        BookmarkResumeCheckpointTransitionKind.InitialTriggerClaim or BookmarkResumeCheckpointTransitionKind.RedeliveryClaim => RuntimeCheckpointNames.ActivityAttemptClaimed,
        BookmarkResumeCheckpointTransitionKind.StaleBookmarkConsumption => RuntimeCheckpointNames.BookmarkConsumed,
        BookmarkResumeCheckpointTransitionKind.ActivitySuspension => RuntimeCheckpointNames.ActivitySuspended,
        BookmarkResumeCheckpointTransitionKind.ActivityCompletion => RuntimeCheckpointNames.ActivityCompleted,
        _ => throw new ArgumentOutOfRangeException(nameof(TransitionKind), TransitionKind, "Unknown bookmark resume checkpoint transition.")
    };
    public string? CommitDiscriminator { get; }
    public ActivityExecutionState ActivityExecutionState { get; }
    public IReadOnlyCollection<RuntimeSchedulerWorkItem> ContinuationWorkItems { get; }
    public IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> ValueSnapshots { get; }

    /// <summary>
    /// Durable-value changes (e.g. the completing activity's workflow-output captures) to commit atomically
    /// with the selected resume transition checkpoint, mirroring how the invoke path folds its capture
    /// projection into the completion commit. Workflow-variable captures do NOT ride this channel — they
    /// travel as <see cref="WorkflowExecutionChange"/> (#972). Empty unless the completion produced captures.
    /// </summary>
    public IReadOnlyCollection<RuntimeStateChange<DurableValueState>> DurableValueChanges { get; }

    /// <summary>
    /// The workflow execution state change carrying a workflow-variable output-capture write-back onto the
    /// canonical root variable frame (#972), committed atomically with the completion transition — the frame
    /// is the single runtime truth for workflow-scope variables. Null unless the completing activity captured
    /// an output into a workflow variable. Completion transitions only.
    /// </summary>
    public RuntimeStateChange<WorkflowExecutionState>? WorkflowExecutionChange { get; }

    public static BookmarkConsumptionCheckpointRequest ForInitialTriggerClaim(
        RuntimeSchedulerWorkItem resumeWorkItem,
        RuntimeResumeBookmarkCommandPayload resumePayload,
        BookmarkState selectedBookmark,
        ActivityExecutionState claimedState,
        string attemptId,
        IReadOnlyCollection<BookmarkState>? siblingBookmarks = null) =>
        new(
            BookmarkResumeCheckpointTransitionKind.InitialTriggerClaim,
            resumeWorkItem,
            resumePayload,
            selectedBookmark,
            siblingBookmarks,
            claimedState,
            [],
            [],
            [],
            attemptId);

    public static BookmarkConsumptionCheckpointRequest ForRedeliveryClaim(
        RuntimeSchedulerWorkItem resumeWorkItem,
        RuntimeResumeBookmarkCommandPayload resumePayload,
        ActivityExecutionState claimedState,
        string attemptId) =>
        new(
            BookmarkResumeCheckpointTransitionKind.RedeliveryClaim,
            resumeWorkItem,
            resumePayload,
            null,
            null,
            claimedState,
            [],
            [],
            [],
            attemptId);

    public static BookmarkConsumptionCheckpointRequest ForStaleBookmarkConsumption(
        RuntimeSchedulerWorkItem resumeWorkItem,
        RuntimeResumeBookmarkCommandPayload resumePayload,
        BookmarkState bookmark,
        ActivityExecutionState completedState,
        RuntimeSchedulerWorkItem completionWorkItem) =>
        new(
            BookmarkResumeCheckpointTransitionKind.StaleBookmarkConsumption,
            resumeWorkItem,
            resumePayload,
            bookmark,
            null,
            completedState,
            [completionWorkItem],
            [],
            [],
            null);

    public static BookmarkConsumptionCheckpointRequest ForSuspension(
        RuntimeSchedulerWorkItem resumeWorkItem,
        RuntimeResumeBookmarkCommandPayload resumePayload,
        ActivityExecutionState suspendedState,
        IReadOnlyCollection<RuntimeSchedulerWorkItem> bookmarkCreationWorkItems,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot>? valueSnapshots = null,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>>? durableValueChanges = null) =>
        new(
            BookmarkResumeCheckpointTransitionKind.ActivitySuspension,
            resumeWorkItem,
            resumePayload,
            null,
            null,
            suspendedState,
            bookmarkCreationWorkItems,
            valueSnapshots ?? [],
            durableValueChanges ?? [],
            null);

    public static BookmarkConsumptionCheckpointRequest ForCompletion(
        RuntimeSchedulerWorkItem resumeWorkItem,
        RuntimeResumeBookmarkCommandPayload resumePayload,
        ActivityExecutionState completedState,
        RuntimeSchedulerWorkItem completionWorkItem,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot>? valueSnapshots = null,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>>? durableValueChanges = null,
        RuntimeStateChange<WorkflowExecutionState>? workflowExecutionChange = null) =>
        new(
            BookmarkResumeCheckpointTransitionKind.ActivityCompletion,
            resumeWorkItem,
            resumePayload,
            null,
            null,
            completedState,
            [completionWorkItem],
            valueSnapshots ?? [],
            durableValueChanges ?? [],
            null,
            workflowExecutionChange);

    private static void ValidateOwnedBookmark(
        string workflowExecutionId,
        ActivityExecutionState state,
        BookmarkState bookmark,
        string parameterName)
    {
        if (!StringComparer.Ordinal.Equals(bookmark.WorkflowExecutionId, workflowExecutionId) ||
            !StringComparer.Ordinal.Equals(bookmark.ActivityExecutionId, state.Execution.ActivityExecutionId) ||
            !StringComparer.Ordinal.Equals(bookmark.ExecutableNodeId, state.Execution.ExecutableNodeId))
            throw new ArgumentException("Every retired sibling bookmark must belong to the resumed activity execution.", parameterName);

    }

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

public enum BookmarkResumeCheckpointTransitionKind
{
    InitialTriggerClaim,
    RedeliveryClaim,
    StaleBookmarkConsumption,
    ActivitySuspension,
    ActivityCompletion
}

public sealed record BookmarkConsumptionCheckpointResult(
    string CommitId,
    string CheckpointId,
    string CheckpointName,
    RuntimeCheckpointCommitResult CommitResult)
{
    public RuntimeCheckpointPersistenceDecision PersistenceDecision => CommitResult.PersistenceDecision;
}
