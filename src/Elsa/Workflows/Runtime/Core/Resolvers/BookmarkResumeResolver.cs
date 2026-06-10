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

        if (executable.Identity != workflowExecution.PinnedExecutable)
            throw NewException(
                bookmark,
                BookmarkResumeResolutionFailureReason.ExecutableArtifactNotPinned,
                $"Executable artifact '{executable.Identity.ArtifactId}' is not the pinned artifact for workflow execution '{workflowExecution.WorkflowExecutionId}'.");

        if (!executable.ResumeTargets.TryGetValue(bookmark.ResumeTargetId, out var resumeTarget))
            throw NewException(
                bookmark,
                BookmarkResumeResolutionFailureReason.ResumeTargetMissing,
                $"Resume target '{bookmark.ResumeTargetId}' is not declared by executable artifact '{executable.Identity.ArtifactId}'.");

        if (resumeTarget.ResumeTargetId != bookmark.ResumeTargetId || resumeTarget.ExecutableNodeId != bookmark.ExecutableNodeId)
            throw NewException(
                bookmark,
                BookmarkResumeResolutionFailureReason.ResumeTargetNodeMismatch,
                $"Resume target '{bookmark.ResumeTargetId}' does not point at executable node '{bookmark.ExecutableNodeId}'.");

        var executableNode = executable.Nodes.FirstOrDefault(node => node.ExecutableNodeId == bookmark.ExecutableNodeId);

        if (executableNode is null)
            throw NewException(
                bookmark,
                BookmarkResumeResolutionFailureReason.ExecutableNodeMissing,
                $"Executable node '{bookmark.ExecutableNodeId}' for bookmark '{bookmark.BookmarkId}' is missing from executable artifact '{executable.Identity.ArtifactId}'.");

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
