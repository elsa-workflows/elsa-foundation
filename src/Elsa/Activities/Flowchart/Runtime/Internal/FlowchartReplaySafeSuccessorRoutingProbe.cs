using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Activities.Flowchart.Internal;

/// <summary>
/// Flowchart implementation of the spec-123 D2 single-predecessor probe (ADR 0047 resolution #1): reads the
/// successor's inbound-edge count off the spec-119 <see cref="FlowchartGraph"/> routing memo. A count above one is a
/// fan-in/join edge and keeps the discrete completion cascade; the flowchart's entry node (zero inbound) is trivially
/// single-predecessor.
/// </summary>
public sealed class FlowchartReplaySafeSuccessorRoutingProbe : IReplaySafeSuccessorRoutingProbe
{
    public int? TryGetInboundCount(ExecutableNode parentCompositeNode, string successorNodeId)
    {
        ArgumentNullException.ThrowIfNull(parentCompositeNode);
        ArgumentException.ThrowIfNullOrWhiteSpace(successorNodeId);

        if (!StringComparer.Ordinal.Equals(parentCompositeNode.Structure?.Kind, FlowchartActivity.StructureKind))
            return null;

        var graph = parentCompositeNode.GetOrAddRoutingStructure(FlowchartGraph.From);
        return graph.GetInboundConnections(successorNodeId).Count;
    }
}
