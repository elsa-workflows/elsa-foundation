using System.Text.Json.Serialization;

namespace Elsa.Activities.Flowchart.Models;

public sealed record FlowchartActiveChild
{
    [JsonConstructor]
    public FlowchartActiveChild(
        string nodeId,
        string executionPathId,
        string executionScopeId,
        string schedulingCause,
        string? childActivityExecutionId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionPathId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionScopeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(schedulingCause);

        ChildActivityExecutionId = childActivityExecutionId;
        NodeId = nodeId;
        ExecutionPathId = executionPathId;
        ExecutionScopeId = executionScopeId;
        SchedulingCause = schedulingCause;
    }

    public string? ChildActivityExecutionId { get; init; }
    public string NodeId { get; init; }
    public string ExecutionPathId { get; init; }
    public string ExecutionScopeId { get; init; }
    public string SchedulingCause { get; init; }
}
