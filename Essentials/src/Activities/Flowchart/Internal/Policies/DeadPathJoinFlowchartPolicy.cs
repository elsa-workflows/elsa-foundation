using Elsa.Activities.Flowchart.Contracts;
using Elsa.Activities.Flowchart.Models;

namespace Elsa.Activities.Flowchart.Internal.Policies;

/// <summary>
/// ADR 0064's join semantics: a target waits until <b>every</b> inbound connection has an arrival for its
/// <c>(node, scope, iteration)</c> key, live or dead, and is scheduled when at least one of them is live.
/// <para>
/// Note what this does not consult: no graph walk, no live-work set, no execution scope beyond the key the
/// arrivals are already partitioned by. "Why is this join waiting" becomes a lookup against one node's arrival
/// set — the inbound sources not in it — which is the answer shape a model whose reason to exist is being
/// easier to reason about than BPMN owes its users. It is decidable inside a loop without special-casing
/// because the arrival key already carries the iteration, so join accounting stops depending on the scope
/// model for its correctness.
/// </para>
/// </summary>
public sealed class DeadPathJoinFlowchartPolicy()
    : MatchingOutboundFlowchartPolicyBase(FlowchartPolicyKinds.DeadPathJoin, "Dead Path Join"), IFlowchartJoinPolicy
{
    public bool PropagatesDeadPaths => true;

    public bool ShouldWait(FlowchartJoinContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var answeredSourceIds = context.Arrivals
            .Select(arrival => arrival.SourceNodeId)
            .ToHashSet(StringComparer.Ordinal);

        return context.InboundConnections.Any(inbound => !answeredSourceIds.Contains(inbound.Source.NodeId));
    }
}
