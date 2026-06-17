using System.Text.Json.Serialization;

namespace Elsa.Activities.Flowchart.Models;

public sealed record FlowchartArrival
{
    [JsonConstructor]
    public FlowchartArrival(
        string arrivalId,
        string executionPathId,
        string executionScopeId,
        string sourceNodeId,
        string targetNodeId,
        string connectionId,
        string sourcePort,
        string producingActivityExecutionId,
        FlowchartArrivalStatus status = FlowchartArrivalStatus.Arrived,
        string? iterationKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(arrivalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionPathId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionScopeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePort);
        ArgumentException.ThrowIfNullOrWhiteSpace(producingActivityExecutionId);

        ArrivalId = arrivalId;
        ExecutionPathId = executionPathId;
        ExecutionScopeId = executionScopeId;
        SourceNodeId = sourceNodeId;
        TargetNodeId = targetNodeId;
        ConnectionId = connectionId;
        SourcePort = sourcePort;
        ProducingActivityExecutionId = producingActivityExecutionId;
        Status = status;
        IterationKey = iterationKey;
    }

    public string ArrivalId { get; init; }
    public string ExecutionPathId { get; init; }
    public string ExecutionScopeId { get; init; }
    public string SourceNodeId { get; init; }
    public string TargetNodeId { get; init; }
    public string ConnectionId { get; init; }
    public string SourcePort { get; init; }
    public string ProducingActivityExecutionId { get; init; }
    public FlowchartArrivalStatus Status { get; init; }
    public string? IterationKey { get; init; }
}

public enum FlowchartArrivalStatus
{
    Arrived,
    Consumed
}
