using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

public sealed record BookmarkResumeRequest(
    WorkflowExecutionState WorkflowExecution,
    WorkflowExecutable Executable,
    BookmarkState Bookmark,
    JsonElement? Input);

public sealed record BookmarkResumeResolution(
    BookmarkState Bookmark,
    ExecutableNode ExecutableNode,
    WorkflowExecutableResumeTarget ResumeTarget,
    JsonElement? Input);
