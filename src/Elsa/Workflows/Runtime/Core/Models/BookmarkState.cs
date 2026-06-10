using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Durable runtime resume handle owned by one activity execution.
/// </summary>
public sealed record BookmarkState(
    string BookmarkId,
    string WorkflowExecutionId,
    string ActivityExecutionId,
    string ExecutableNodeId,
    string ResumeTargetId,
    string StimulusType,
    string StimulusHash,
    JsonElement? Payload,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt);
