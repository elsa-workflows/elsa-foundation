using System.Text.Json.Serialization;

namespace Elsa.Activities.Flowchart.Models;

public sealed record FlowchartExecutionState
{
    [JsonConstructor]
    public FlowchartExecutionState(
        string rootExecutionScopeId,
        IReadOnlyCollection<ExecutionScope>? scopes = null,
        IReadOnlyCollection<ExecutionPath>? executionPaths = null,
        IReadOnlyCollection<FlowchartArrival>? arrivals = null,
        IReadOnlyCollection<FlowchartActiveChild>? activeChildren = null,
        IReadOnlyCollection<FlowchartDiagnosticEvent>? diagnostics = null,
        int sequence = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootExecutionScopeId);

        RootExecutionScopeId = rootExecutionScopeId;
        Scopes = scopes ?? [];
        ExecutionPaths = executionPaths ?? [];
        Arrivals = arrivals ?? [];
        ActiveChildren = activeChildren ?? [];
        Diagnostics = diagnostics ?? [];
        Sequence = sequence;
    }

    public string RootExecutionScopeId { get; init; }
    public IReadOnlyCollection<ExecutionScope> Scopes { get; init; }
    public IReadOnlyCollection<ExecutionPath> ExecutionPaths { get; init; }
    public IReadOnlyCollection<FlowchartArrival> Arrivals { get; init; }
    public IReadOnlyCollection<FlowchartActiveChild> ActiveChildren { get; init; }
    public IReadOnlyCollection<FlowchartDiagnosticEvent> Diagnostics { get; init; }
    public int Sequence { get; init; }
}
