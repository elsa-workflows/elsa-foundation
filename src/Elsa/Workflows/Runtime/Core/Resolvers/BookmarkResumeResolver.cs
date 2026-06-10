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

        if (!MatchesPinnedExecutable(executable.Identity, workflowExecution.PinnedExecutable))
            throw NewException(
                bookmark,
                BookmarkResumeResolutionFailureReason.ExecutableArtifactNotPinned,
                $"Executable artifact '{FormatIdentity(executable.Identity)}' is not the pinned artifact '{FormatIdentity(workflowExecution.PinnedExecutable)}' for workflow execution '{workflowExecution.WorkflowExecutionId}'.");

        if (!executable.ResumeTargets.TryGetValue(bookmark.ResumeTargetId, out var resumeTarget))
            throw NewException(
                bookmark,
                BookmarkResumeResolutionFailureReason.ResumeTargetMissing,
                $"Resume target '{bookmark.ResumeTargetId}' is not declared by executable artifact '{FormatIdentity(executable.Identity)}'.");

        if (resumeTarget.ResumeTargetId != bookmark.ResumeTargetId)
            throw NewException(
                bookmark,
                BookmarkResumeResolutionFailureReason.ResumeTargetIdentityMismatch,
                $"Resume target table entry '{bookmark.ResumeTargetId}' contains target ID '{resumeTarget.ResumeTargetId}'.");

        if (resumeTarget.ExecutableNodeId != bookmark.ExecutableNodeId)
            throw NewException(
                bookmark,
                BookmarkResumeResolutionFailureReason.ResumeTargetNodeMismatch,
                $"Resume target '{bookmark.ResumeTargetId}' does not point at executable node '{bookmark.ExecutableNodeId}'.");

        if (!executable.NodesById.TryGetValue(bookmark.ExecutableNodeId, out var executableNode))
            throw NewException(
                bookmark,
                BookmarkResumeResolutionFailureReason.ExecutableNodeMissing,
                $"Executable node '{bookmark.ExecutableNodeId}' for bookmark '{bookmark.BookmarkId}' is missing from executable artifact '{FormatIdentity(executable.Identity)}'.");

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

    private static bool MatchesPinnedExecutable(WorkflowExecutableIdentity executable, WorkflowExecutableIdentity pinned) =>
        executable.ArtifactId == pinned.ArtifactId &&
        executable.DefinitionId == pinned.DefinitionId &&
        executable.DefinitionVersionId == pinned.DefinitionVersionId &&
        executable.ArtifactVersion == pinned.ArtifactVersion &&
        executable.ArtifactHash == pinned.ArtifactHash;

    private static string FormatIdentity(WorkflowExecutableIdentity identity) =>
        $"{identity.ArtifactId}@{identity.ArtifactVersion}/{identity.ArtifactHash}";
}
