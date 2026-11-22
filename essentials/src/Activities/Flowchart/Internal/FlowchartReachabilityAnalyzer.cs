using Elsa.Activities.Flowchart.Exceptions;

namespace Elsa.Activities.Flowchart.Internal;

public sealed class FlowchartReachabilityAnalyzer
{
    public bool CanReach(FlowchartGraph graph, string sourceNodeId, string targetNodeId) =>
        graph.CanReach(sourceNodeId, targetNodeId);

    /// <inheritdoc cref="FlowchartGraph.CanReachForward"/>
    public bool CanReachForward(FlowchartGraph graph, string sourceNodeId, string targetNodeId) =>
        graph.CanReachForward(sourceNodeId, targetNodeId);

    public bool IsBackwardEdge(FlowchartGraph graph, string sourceNodeId, string targetNodeId) =>
        graph.IsBackwardEdge(sourceNodeId, targetNodeId);

    public void ValidateNoAmbiguousLoopbacks(FlowchartGraph graph)
    {
        foreach (var connection in graph.Connections)
        {
            if (!IsBackwardEdge(graph, connection.Source.NodeId, connection.Target.NodeId))
                continue;

            if (graph.GetInboundConnections(connection.Target.NodeId).Count > 1)
                throw new FlowchartExecutionException($"Flowchart loopback from '{connection.Source.NodeId}' to '{connection.Target.NodeId}' is ambiguous because the loop target has multiple inbound connections.");
        }
    }
}
