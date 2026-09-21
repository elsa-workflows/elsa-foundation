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

/// <summary>
/// Persisted as an <b>ordinal</b> (§E6 wire surface, pinned by the golden fixture), so members may be appended
/// but never reordered.
/// </summary>
public enum FlowchartArrivalStatus
{
    /// <summary>A live token reached the target over this connection.</summary>
    Arrived,

    /// <summary>The target consumed this arrival — it fired, or carried the path's deadness forward.</summary>
    Consumed,

    /// <summary>
    /// The source completed <em>without</em> taking this connection, so nothing will ever traverse it for this
    /// <c>(node, scope, iteration)</c> key. ADR 0064: deadness is a token state rather than something a join
    /// re-derives by searching the graph for live work. A join counts a dead arrival towards "every inbound has
    /// answered" but never towards "something live got here", so it fires only when at least one arrival is
    /// <see cref="Arrived"/>.
    /// </summary>
    Dead
}
