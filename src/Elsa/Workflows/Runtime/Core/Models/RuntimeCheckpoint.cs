using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Named runtime boundary describing what changed. Persistence policy decides when it is flushed.
/// </summary>
public sealed record RuntimeCheckpoint(
    string CheckpointId,
    string Name,
    string WorkflowExecutionId,
    DateTimeOffset OccurredAt,
    IReadOnlyCollection<string> ActivityExecutionIds,
    IReadOnlyDictionary<string, string> Metadata)
{
    /// <summary>Generic replay-stable provenance. Null only before preparation or on legacy serialized checkpoints.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RuntimeCheckpointProvenance? Provenance { get; init; }
}

public sealed record RuntimeCheckpointPersistenceDecision(
    RuntimeCheckpointPersistenceMode Mode,
    string? Reason = null);

public enum RuntimeCheckpointPersistenceMode
{
    Immediate,
    Deferred,
    Skip
}
