using Elsa.Workflows.Runtime.Core.Constants;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Builds the durable bookmark and suspended activity state for one bookmark-created checkpoint.</summary>
public static class BookmarkSuspension
{
    public const string SuspendedSubStatus = "BookmarkWaiting";

    public static (BookmarkState Bookmark, ActivityExecutionState ActivityExecution) Build(
        RuntimeSchedulerWorkItem workItem,
        RuntimeCreateBookmarkCommandPayload payload,
        ActivityExecutionState activityExecution,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(activityExecution);

        var bookmarkMetadata = payload.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        bookmarkMetadata[RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId;
        bookmarkMetadata[RuntimeMetadataKeys.CommandId] = workItem.CommandId;
        bookmarkMetadata[RuntimeMetadataKeys.Reason] = payload.Reason;
        var bookmark = new BookmarkState(
            BookmarkId: payload.BookmarkId,
            WorkflowExecutionId: workItem.WorkflowExecutionId,
            ActivityExecutionId: payload.ActivityExecutionId,
            ExecutableNodeId: payload.ExecutableNodeId,
            ResumeTargetId: payload.ResumeTargetId,
            StimulusType: payload.StimulusType,
            StimulusHash: payload.StimulusHash,
            Payload: payload.Payload,
            Metadata: RuntimeModelMetadata.Snapshot(bookmarkMetadata),
            CreatedAt: createdAt,
            ExpiresAt: payload.ExpiresAt);

        var bookmarkIds = activityExecution.BookmarkIds
            .Append(payload.BookmarkId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var activityMetadata = activityExecution.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        activityMetadata[RuntimeMetadataKeys.BookmarkId] = payload.BookmarkId;
        activityMetadata[RuntimeMetadataKeys.ResumeTargetId] = payload.ResumeTargetId;
        activityMetadata[RuntimeMetadataKeys.SuspendReason] = payload.Reason;
        activityMetadata[RuntimeMetadataKeys.CreateBookmarkSchedulerWorkItemId] = workItem.WorkItemId;
        var suspendedActivity = activityExecution with
        {
            Status = ActivityExecutionStatus.Suspended,
            SubStatus = SuspendedSubStatus,
            BookmarkIds = bookmarkIds,
            Metadata = RuntimeModelMetadata.Snapshot(activityMetadata)
        };

        return (bookmark, suspendedActivity);
    }
}
