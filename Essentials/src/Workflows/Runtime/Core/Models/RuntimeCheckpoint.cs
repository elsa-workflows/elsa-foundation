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
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public RuntimeHistoryCausation? HistoryCausation { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? HistoryStartsAtAdmission { get; init; }
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
