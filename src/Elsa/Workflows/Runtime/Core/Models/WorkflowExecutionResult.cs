namespace Elsa.Workflows.Runtime.Core.Models;

public sealed record WorkflowExecutionResult(
    string WorkflowExecutionId,
    string ArtifactId,
    WorkflowExecutionResultStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<ActivityExecutionResult> Activities,
    string? Error);

public sealed record ActivityExecutionResult(
    string ActivityExecutionId,
    string ExecutableNodeId,
    string ActivityType,
    ActivityExecutionResultStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? Error);

public enum WorkflowExecutionResultStatus
{
    Completed,
    Faulted
}

public enum ActivityExecutionResultStatus
{
    Completed,
    Skipped,
    Faulted
}
