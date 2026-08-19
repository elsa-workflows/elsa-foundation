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
        int sequence = 0,
        IReadOnlyDictionary<string, int>? loopIterationCounters = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootExecutionScopeId);

        RootExecutionScopeId = rootExecutionScopeId;
        Scopes = scopes ?? [];
        ExecutionPaths = executionPaths ?? [];
        Arrivals = arrivals ?? [];
        ActiveChildren = activeChildren ?? [];
        Diagnostics = diagnostics ?? [];
        Sequence = sequence;
        LoopIterationCounters = loopIterationCounters ?? new Dictionary<string, int>();
    }

    public string RootExecutionScopeId { get; init; }
    public IReadOnlyCollection<ExecutionScope> Scopes { get; init; }
    public IReadOnlyCollection<ExecutionPath> ExecutionPaths { get; init; }
    public IReadOnlyCollection<FlowchartArrival> Arrivals { get; init; }
    public IReadOnlyCollection<FlowchartActiveChild> ActiveChildren { get; init; }
    public IReadOnlyCollection<FlowchartDiagnosticEvent> Diagnostics { get; init; }
    public int Sequence { get; init; }

    /// <summary>
    /// Explicit monotonic loop-iteration counter per loop owner node (the backward-edge target). The value
    /// is the highest iteration number minted so far for that owner; it only ever increases, which
    /// decouples iteration-key numbering from the live loop-iteration scope count and lets stale scopes be
    /// pruned without a later iteration reusing an earlier key (#382 / W32).
    /// </summary>
    public IReadOnlyDictionary<string, int> LoopIterationCounters { get; init; }
}
