using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Resolvers;

public sealed class BookmarkResumeResolver : IBookmarkResumeResolver
{
    public BookmarkResumeResolution Resolve(BookmarkResumeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var workflowExecution = request.WorkflowExecution;
        var executable = request.Executable;
        var bookmark = request.Bookmark;

        if (bookmark.WorkflowExecutionId != workflowExecution.WorkflowExecutionId)
            throw NewException(
                bookmark,
                BookmarkResumeResolutionFailureReason.WorkflowExecutionMismatch,
                $"Bookmark '{bookmark.BookmarkId}' belongs to workflow execution '{bookmark.WorkflowExecutionId}', not '{workflowExecution.WorkflowExecutionId}'.");

        if (!WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(executable.Identity, workflowExecution.PinnedExecutable))
            throw NewException(
                bookmark,
                BookmarkResumeResolutionFailureReason.ExecutableArtifactNotPinned,
                $"Executable artifact '{WorkflowExecutableIdentityComparer.Format(executable.Identity)}' is not the pinned artifact '{WorkflowExecutableIdentityComparer.Format(workflowExecution.PinnedExecutable)}' for workflow execution '{workflowExecution.WorkflowExecutionId}'.");

        if (!executable.ResumeTargets.TryGetValue(bookmark.ResumeTargetId, out var resumeTarget))
            throw NewException(
                bookmark,
                BookmarkResumeResolutionFailureReason.ResumeTargetMissing,
                $"Resume target '{bookmark.ResumeTargetId}' is not declared by executable artifact '{WorkflowExecutableIdentityComparer.Format(executable.Identity)}'.");

        if (resumeTarget.ResumeTargetId != bookmark.ResumeTargetId)
            throw NewException(
                bookmark,
                BookmarkResumeResolutionFailureReason.ResumeTargetIdentityMismatch,
                $"Resume target table entry '{bookmark.ResumeTargetId}' contains target ID '{resumeTarget.ResumeTargetId}'.");

        if (resumeTarget.ExecutableNodeId != bookmark.ExecutableNodeId)
            throw NewException(
                bookmark,
                BookmarkResumeResolutionFailureReason.ResumeTargetNodeMismatch,
                $"Resume target '{bookmark.ResumeTargetId}' points at executable node '{resumeTarget.ExecutableNodeId}', not bookmark executable node '{bookmark.ExecutableNodeId}'.");

        if (!executable.NodesById.TryGetValue(bookmark.ExecutableNodeId, out var executableNode))
            throw NewException(
                bookmark,
                BookmarkResumeResolutionFailureReason.ExecutableNodeMissing,
                $"Executable node '{bookmark.ExecutableNodeId}' for bookmark '{bookmark.BookmarkId}' is missing from executable artifact '{WorkflowExecutableIdentityComparer.Format(executable.Identity)}'.");

        return new BookmarkResumeResolution(bookmark, executableNode, resumeTarget, request.Input);
    }

    private static BookmarkResumeResolutionException NewException(
        BookmarkState bookmark,
        BookmarkResumeResolutionFailureReason reason,
        string message) =>
        new(
            message,
            reason,
            bookmark.WorkflowExecutionId,
            bookmark.BookmarkId,
            bookmark.ResumeTargetId);

}
