namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// A durable index entry mapping an external stimulus identity to a start-trigger activity inside a
/// published workflow executable (W7, E3-1). It is written at publish time and read by the stimulus
/// router to start a new workflow instance when a matching stimulus arrives — the piece Elsa 4 was
/// missing that made "start a workflow from an external event" impossible.
/// </summary>
public sealed record WorkflowTriggerBinding(
    string TriggerBindingId,
    string ArtifactId,
    string DefinitionId,
    string ArtifactVersion,
    string ArtifactHash,
    string ExecutableNodeId,
    string StimulusType,
    string StimulusHash,
    string? CorrelationScope,
    IReadOnlyDictionary<string, string> Metadata,
    DateTimeOffset CreatedAt)
{
    /// <summary>
    /// Builds the deterministic, collision-free binding id for a trigger node in an artifact. Parts are
    /// escaped so a separator inside an id cannot forge a different (artifactId, executableNodeId) pair.
    /// </summary>
    public static string BuildId(string artifactId, string executableNodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);
        return $"{Escape(artifactId)}:{Escape(executableNodeId)}";
    }

    private static string Escape(string value) => value.Replace("%", "%25").Replace(":", "%3A");
}
