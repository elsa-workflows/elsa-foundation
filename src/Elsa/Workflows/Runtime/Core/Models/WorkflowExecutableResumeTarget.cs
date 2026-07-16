using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Stable target inside a pinned executable artifact used to resolve durable resume handles.
/// </summary>
[method: JsonConstructor]
public sealed record WorkflowExecutableResumeTarget(
    string ResumeTargetId,
    string ExecutableNodeId,
    string HandlerKey,
    IReadOnlyDictionary<string, string> Metadata,
    string? LocalResumeTargetId = null)
{
    public WorkflowExecutableResumeTarget(
        string resumeTargetId,
        string executableNodeId,
        string handlerKey,
        IReadOnlyDictionary<string, string> metadata)
        : this(resumeTargetId, executableNodeId, handlerKey, metadata, null)
    {
    }
}
