using System.Text.Json.Serialization;

namespace Elsa.Activities.Flowchart.Models;

public sealed record FlowchartPolicyCommand
{
    [JsonConstructor]
    public FlowchartPolicyCommand(
        FlowchartPolicyCommandKind kind,
        string? nodeId = null,
        string? connectionId = null,
        string? executionPathId = null,
        string? executionScopeId = null,
        string? targetExecutionScopeId = null,
        string? message = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        Kind = kind;
        NodeId = nodeId;
        ConnectionId = connectionId;
        ExecutionPathId = executionPathId;
        ExecutionScopeId = executionScopeId;
        TargetExecutionScopeId = targetExecutionScopeId;
        Message = message;
        Metadata = metadata ?? new Dictionary<string, string>();
    }

    public FlowchartPolicyCommandKind Kind { get; init; }
    public string? NodeId { get; init; }
    public string? ConnectionId { get; init; }
    public string? ExecutionPathId { get; init; }
    public string? ExecutionScopeId { get; init; }
    public string? TargetExecutionScopeId { get; init; }
    public string? Message { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
}

public enum FlowchartPolicyCommandKind
{
    CreateScope,
    CreateExecutionPath,
    MoveExecutionPath,
    WaitExecutionPath,
    CompleteExecutionPath,
    CancelExecutionPath,
    ScheduleNode,
    RecordArrival,
    ConsumeArrival,
    CancelScope,
    CompleteScope,
    WriteDiagnostic
}
