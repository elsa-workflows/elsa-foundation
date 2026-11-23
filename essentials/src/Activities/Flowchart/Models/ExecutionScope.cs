using System.Text.Json.Serialization;

namespace Elsa.Activities.Flowchart.Models;

public sealed record ExecutionScope
{
    [JsonConstructor]
    public ExecutionScope(
        string executionScopeId,
        ExecutionScopeKind kind,
        string? parentExecutionScopeId = null,
        string? createdByNodeId = null,
        string? startConnectionId = null,
        string? ownerNodeId = null,
        string? loopIterationKey = null,
        ExecutionScopeStatus status = ExecutionScopeStatus.Active,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionScopeId);

        ExecutionScopeId = executionScopeId;
        Kind = kind;
        ParentExecutionScopeId = parentExecutionScopeId;
        CreatedByNodeId = createdByNodeId;
        StartConnectionId = startConnectionId;
        OwnerNodeId = ownerNodeId;
        LoopIterationKey = loopIterationKey;
        Status = status;
        Metadata = metadata ?? new Dictionary<string, string>();
    }

    public string ExecutionScopeId { get; init; }
    public ExecutionScopeKind Kind { get; init; }
    public string? ParentExecutionScopeId { get; init; }
    public string? CreatedByNodeId { get; init; }
    public string? StartConnectionId { get; init; }
    public string? OwnerNodeId { get; init; }
    public string? LoopIterationKey { get; init; }
    public ExecutionScopeStatus Status { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
}

public enum ExecutionScopeKind
{
    Root,
    Branch,
    Join,
    LoopIteration,
    Race
}

public enum ExecutionScopeStatus
{
    Active,
    Waiting,
    Completed,
    Canceled,
    Faulted
}
