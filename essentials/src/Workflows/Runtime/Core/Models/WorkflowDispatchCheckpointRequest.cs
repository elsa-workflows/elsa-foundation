using Elsa.Activities.Runtime.Core.Models;
namespace Elsa.Workflows.Runtime.Core.Models;

public sealed class WorkflowDispatchCheckpointRequest
{
    public WorkflowDispatchCheckpointRequest(WorkflowDispatchRecord record, RuntimePostCommitIntent startIntent)
        : this(record, startIntent, waitBookmark: null, expectedWaitResumeTargetId: null, expectedWaitStimulusType: null)
    {
    }

    public WorkflowDispatchCheckpointRequest(
        WorkflowDispatchRecord record,
        RuntimePostCommitIntent startIntent,
        ActivityBookmarkRequest? waitBookmark,
        string? expectedWaitResumeTargetId,
        string? expectedWaitStimulusType)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(startIntent);
        if (!StringComparer.Ordinal.Equals(record.ParentWorkflowExecutionId, startIntent.WorkflowExecutionId))
            throw new ArgumentException("The start intent workflow execution ID must match the dispatch parent execution.", nameof(startIntent));
        if (!StringComparer.Ordinal.Equals(record.ParentActivityExecutionId, startIntent.ActivityExecutionId))
            throw new ArgumentException("The start intent activity execution ID must match the dispatch parent activity execution.", nameof(startIntent));
        var identity = new WorkflowDispatchIdentity(record.ParentWorkflowExecutionId, record.ParentActivityExecutionId);
        if (!StringComparer.Ordinal.Equals(record.DispatchId, identity.DispatchId) ||
            !StringComparer.Ordinal.Equals(record.ChildWorkflowExecutionId, identity.ChildWorkflowExecutionId))
        {
            throw new ArgumentException("The dispatch record does not match its deterministic parent/activity identity.", nameof(record));
        }
        if (!StringComparer.Ordinal.Equals(startIntent.IntentId, identity.StartIntentId) ||
            !StringComparer.Ordinal.Equals(startIntent.IdempotencyKey, identity.StartIdempotencyKey))
        {
            throw new ArgumentException("The start intent does not match the dispatch record's deterministic identity.", nameof(startIntent));
        }

        if (record.Mode == WorkflowDispatchMode.FireAndForget && waitBookmark is not null)
            throw new ArgumentException("A fire-and-forget dispatch cannot carry a wait bookmark.", nameof(waitBookmark));
        if (record.Mode == WorkflowDispatchMode.WaitForCompletion && waitBookmark is null)
            throw new ArgumentNullException(nameof(waitBookmark), "A wait-for-completion dispatch requires a bookmark.");
        if (waitBookmark is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(expectedWaitResumeTargetId);
            ArgumentException.ThrowIfNullOrWhiteSpace(expectedWaitStimulusType);
            if (!StringComparer.Ordinal.Equals(waitBookmark.BookmarkId, identity.WaitBookmarkId) ||
                !StringComparer.Ordinal.Equals(waitBookmark.StimulusHash, identity.WaitStimulusHash))
            {
                throw new ArgumentException("The wait bookmark does not match the dispatch record's deterministic identity.", nameof(waitBookmark));
            }
            if (!StringComparer.Ordinal.Equals(waitBookmark.ResumeTargetId, expectedWaitResumeTargetId) ||
                !StringComparer.Ordinal.Equals(waitBookmark.StimulusType, expectedWaitStimulusType))
            {
                throw new ArgumentException("The wait bookmark does not match the dispatch activity's canonical completion route.", nameof(waitBookmark));
            }

            if (waitBookmark.ExpiresAt is not null)
                throw new ArgumentException("A DispatchWorkflow wait bookmark cannot expire.", nameof(waitBookmark));
        }

        Record = record;
        StartIntent = startIntent;
        WaitBookmark = waitBookmark;
        ExpectedWaitResumeTargetId = expectedWaitResumeTargetId;
        ExpectedWaitStimulusType = expectedWaitStimulusType;
    }

    public WorkflowDispatchRecord Record { get; }
    public RuntimePostCommitIntent StartIntent { get; }
    public ActivityBookmarkRequest? WaitBookmark { get; }
    public string? ExpectedWaitResumeTargetId { get; }
    public string? ExpectedWaitStimulusType { get; }
}
