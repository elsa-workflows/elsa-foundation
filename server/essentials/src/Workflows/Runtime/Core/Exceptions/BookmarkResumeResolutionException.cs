namespace Elsa.Workflows.Runtime.Core.Exceptions;

public sealed class BookmarkResumeResolutionException(
    string message,
    BookmarkResumeResolutionFailureReason reason,
    string workflowExecutionId,
    string bookmarkId,
    string resumeTargetId) : InvalidOperationException(message)
{
    public BookmarkResumeResolutionFailureReason Reason { get; } = reason;
    public string WorkflowExecutionId { get; } = workflowExecutionId;
    public string BookmarkId { get; } = bookmarkId;
    public string ResumeTargetId { get; } = resumeTargetId;
}

public enum BookmarkResumeResolutionFailureReason
{
    WorkflowExecutionMismatch,
    ExecutableArtifactNotPinned,
    ResumeTargetMissing,
    ResumeTargetIdentityMismatch,
    ResumeTargetNodeMismatch,
    ExecutableNodeMissing
}
