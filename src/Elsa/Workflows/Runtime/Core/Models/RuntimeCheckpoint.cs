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
    IReadOnlyDictionary<string, string> Metadata);

public sealed record RuntimeCheckpointPersistenceDecision(
    RuntimeCheckpointPersistenceMode Mode,
    string? Reason = null);

public enum RuntimeCheckpointPersistenceMode
{
    Immediate,
    Deferred,
    Skip
}
