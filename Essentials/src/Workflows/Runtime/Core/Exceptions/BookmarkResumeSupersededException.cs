namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>The exact saved wait is retired, replaced or no longer eligible. No resume was admitted.</summary>
public sealed class BookmarkResumeSupersededException(string workflowExecutionId, string bookmarkId)
    : Exception("The exact saved bookmark is no longer eligible for resume admission.")
{
    public string WorkflowExecutionId { get; } = workflowExecutionId;
    public string BookmarkId { get; } = bookmarkId;
}
