namespace Elsa.Workflows.Runtime.Core.Constants;

public static class RuntimeCheckpointNames
{
    public const string WorkflowStarted = nameof(WorkflowStarted);
    public const string ActivityScheduled = nameof(ActivityScheduled);
    public const string ActivityStarted = nameof(ActivityStarted);
    public const string ActivityCompleted = nameof(ActivityCompleted);
    public const string ActivitySuspended = nameof(ActivitySuspended);
    public const string BookmarkCreated = nameof(BookmarkCreated);
    public const string BookmarkConsumed = nameof(BookmarkConsumed);
    public const string DurableValueCaptured = nameof(DurableValueCaptured);
    public const string IncidentRecorded = nameof(IncidentRecorded);
    public const string WorkflowSuspended = nameof(WorkflowSuspended);
    public const string WorkflowCompleted = nameof(WorkflowCompleted);
    public const string WorkflowFaulted = nameof(WorkflowFaulted);
    public const string WorkflowCancelled = nameof(WorkflowCancelled);
    public const string PostCommitIntentRecorded = nameof(PostCommitIntentRecorded);

    public static readonly IReadOnlyCollection<string> All =
    [
        WorkflowStarted,
        ActivityScheduled,
        ActivityStarted,
        ActivityCompleted,
        ActivitySuspended,
        BookmarkCreated,
        BookmarkConsumed,
        DurableValueCaptured,
        IncidentRecorded,
        WorkflowSuspended,
        WorkflowCompleted,
        WorkflowFaulted,
        WorkflowCancelled,
        PostCommitIntentRecorded
    ];
}
