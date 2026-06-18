using System.Text.Json.Serialization;

namespace Elsa.Activities.Flowchart.Models;

public sealed record FlowchartDiagnosticEvent
{
    [JsonConstructor]
    public FlowchartDiagnosticEvent(
        string diagnosticId,
        FlowchartDiagnosticKind kind,
        string message,
        string? nodeId = null,
        string? connectionId = null,
        string? executionPathId = null,
        string? executionScopeId = null,
        IReadOnlyDictionary<string, string>? details = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticId);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        DiagnosticId = diagnosticId;
        Kind = kind;
        NodeId = nodeId;
        ConnectionId = connectionId;
        ExecutionPathId = executionPathId;
        ExecutionScopeId = executionScopeId;
        Message = message;
        Details = details ?? new Dictionary<string, string>();
    }

    public string DiagnosticId { get; init; }
    public FlowchartDiagnosticKind Kind { get; init; }
    public string? NodeId { get; init; }
    public string? ConnectionId { get; init; }
    public string? ExecutionPathId { get; init; }
    public string? ExecutionScopeId { get; init; }
    public string Message { get; init; }
    public IReadOnlyDictionary<string, string> Details { get; init; }
}

public enum FlowchartDiagnosticKind
{
    Scheduled,
    Waiting,
    Joined,
    DeadPath,
    LoopIteration,
    Canceled,
    PolicyFailure,
    Completed
}
