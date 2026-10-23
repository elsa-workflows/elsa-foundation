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

    /// <summary>
    /// Composes the node-scoped resume-target id for a local resume-target id declared by an activity:
    /// activities register by local id, the runtime rescopes to the owning executable node.
    /// </summary>
    public static string ComposeScopedId(string executableNodeId, string localResumeTargetId) =>
        $"{executableNodeId}:{localResumeTargetId}";

    /// <summary>
    /// Finds a global target first, otherwise the unique target with the requested node-local key.
    /// The caller must verify that a directly addressed target belongs to the expected node.
    /// </summary>
    public static WorkflowExecutableResumeTarget? FindForNode(
        WorkflowExecutable executable,
        string executableNodeId,
        string requestedResumeTargetId)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedResumeTargetId);

        if (executable.ResumeTargets.TryGetValue(requestedResumeTargetId, out var direct))
            return direct;

        var matches = executable.ResumeTargets.Values
            .Where(target => StringComparer.Ordinal.Equals(target.ExecutableNodeId, executableNodeId))
            .Where(target => StringComparer.Ordinal.Equals(target.LocalResumeTargetId, requestedResumeTargetId))
            .ToArray();
        return matches.Length switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException(
                $"Local resume target '{requestedResumeTargetId}' is ambiguous for executable node '{executableNodeId}'.")
        };
    }
}
