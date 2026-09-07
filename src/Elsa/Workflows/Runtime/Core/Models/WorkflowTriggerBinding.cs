using System.Globalization;
using System.Security.Cryptography;
using System.Text;

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
    DateTimeOffset CreatedAt,
    string? ActivationId = null,
    string? SlotId = null,
    TriggerCardinality Cardinality = TriggerCardinality.FanOut,
    bool IsActive = true)
{
    public const int MaximumIdLength = 128;

    /// <summary>
    /// Builds the deterministic, collision-resistant binding id for a single trigger stimulus on a node in an
    /// artifact. Length-prefixed parts are hashed into one portable bounded identifier. The stimulus hash is part
    /// of the key because one node can emit several descriptors (e.g. one HTTP method each); two descriptors on
    /// the same node must not collide. The id is a pure function of its inputs (no randomness) so republish's
    /// delete-and-resave, which keys on the artifact, deterministically supersedes the prior generation.
    /// </summary>
    public static string BuildId(string artifactId, string executableNodeId, string stimulusHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusHash);
        return BuildIdCore(artifactId, executableNodeId, stimulusHash);
    }

    /// <summary>Builds an activation-scoped binding id so named slots may share one artifact safely.</summary>
    public static string BuildId(string activationId, string artifactId, string executableNodeId, string stimulusHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusHash);
        return BuildIdCore(activationId, artifactId, executableNodeId, stimulusHash);
    }

    public static void ValidateId(string triggerBindingId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(triggerBindingId);
        if (triggerBindingId.Length > MaximumIdLength)
        {
            throw new ArgumentException(
                $"A workflow trigger-binding id cannot exceed {MaximumIdLength} characters.",
                nameof(triggerBindingId));
        }
    }

    private static string BuildIdCore(params string[] parts)
    {
        var canonical = string.Concat(parts.Select(part =>
            $"{part.Length.ToString(CultureInfo.InvariantCulture)}:{part}"));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return $"tb1-{Convert.ToHexString(hash)}";
    }
}

/// <summary>Declares whether multiple authoritative publications may receive one stimulus.</summary>
public enum TriggerCardinality
{
    /// <summary>At most one authoritative publication in the shell may claim the stimulus.</summary>
    Exclusive,

    /// <summary>Multiple authoritative publications may intentionally receive the stimulus.</summary>
    FanOut
}
