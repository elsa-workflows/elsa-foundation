using System.Text.Json.Serialization;

namespace Elsa.Activities.Flowchart.Models;

public sealed record ExecutionPath
{
    [JsonConstructor]
    public ExecutionPath(
        string executionPathId,
        string executionScopeId,
        string? parentExecutionPathId = null,
        string? currentNodeId = null,
        string? incomingConnectionId = null,
        string? schedulingActivityExecutionId = null,
        ExecutionPathStatus status = ExecutionPathStatus.Active,
        string? iterationKey = null,
        IReadOnlyCollection<string>? lastOutcomeNames = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionPathId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionScopeId);

        ExecutionPathId = executionPathId;
        ParentExecutionPathId = parentExecutionPathId;
        ExecutionScopeId = executionScopeId;
        CurrentNodeId = currentNodeId;
        IncomingConnectionId = incomingConnectionId;
        SchedulingActivityExecutionId = schedulingActivityExecutionId;
        Status = status;
        IterationKey = iterationKey;
        LastOutcomeNames = lastOutcomeNames ?? [];
    }

    public string ExecutionPathId { get; init; }
    public string? ParentExecutionPathId { get; init; }
    public string ExecutionScopeId { get; init; }
    public string? CurrentNodeId { get; init; }
    public string? IncomingConnectionId { get; init; }
    public string? SchedulingActivityExecutionId { get; init; }
    public ExecutionPathStatus Status { get; init; }
    public string? IterationKey { get; init; }
    public IReadOnlyCollection<string> LastOutcomeNames { get; init; }
}

public enum ExecutionPathStatus
{
    Active,
    Waiting,
    Completed,
    Canceled,
    Faulted
}
