using System.Text.Json.Serialization;

namespace Kernel.Graph.Models;

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

    // One-step built-in selection evidence; never serialized or recovered by wire ID.
    [JsonIgnore]
    internal FlowchartConnection? SelectedConnection { get; init; }

    // Record copies retain the exact selection for scope and diagnostic edits. Changing a
    // routing field hands selection back to the public command's normal lookup rules.
    public FlowchartPolicyCommandKind Kind
    {
        get;
        init
        {
            if (field != value) SelectedConnection = null;
            field = value;
        }
    }

    public string? NodeId
    {
        get;
        init
        {
            if (field != value) SelectedConnection = null;
            field = value;
        }
    }

    public string? ConnectionId
    {
        get;
        init
        {
            if (field != value) SelectedConnection = null;
            field = value;
        }
    }

    public string? ExecutionPathId { get; init; }
    public string? ExecutionScopeId { get; init; }
    public string? TargetExecutionScopeId { get; init; }
    public string? Message { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; }

    // Transient selection evidence is not part of this public record's value.
    private (FlowchartPolicyCommandKind, string?, string?, string?, string?, string?, string?, IReadOnlyDictionary<string, string>) Value =>
        (Kind, NodeId, ConnectionId, ExecutionPathId, ExecutionScopeId, TargetExecutionScopeId, Message, Metadata);

    public bool Equals(FlowchartPolicyCommand? other) => other is not null && Value.Equals(other.Value);
    public override int GetHashCode() => Value.GetHashCode();
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
