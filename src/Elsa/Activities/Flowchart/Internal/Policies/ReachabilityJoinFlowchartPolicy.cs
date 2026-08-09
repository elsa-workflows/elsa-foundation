using Elsa.Activities.Flowchart.Contracts;
using Elsa.Activities.Flowchart.Models;

namespace Elsa.Activities.Flowchart.Internal.Policies;

/// <summary>
/// The historical join semantics: wait while some live child in the same execution scope can still reach an
/// inbound source that has not arrived. Deadness is never recorded, only re-derived — a downstream join learns
/// that a decision's untaken branch is dead by proving no live token can reach it.
/// <para>
/// The search runs over the graph's forward projection (ADR 0064 WU-1). Over the full connection set it could
/// not tell "a token can still get there" from "both nodes sit in the same loop", and only the
/// <see cref="FlowchartExecutionState.ActiveChildren"/> filter on the execution scope kept it from deadlocking
/// inside one.
/// </para>
/// </summary>
public sealed class ReachabilityJoinFlowchartPolicy()
    : MatchingOutboundFlowchartPolicyBase(FlowchartPolicyKinds.ReachabilityJoin, "Reachability Join"), IFlowchartJoinPolicy
{
    public bool PropagatesDeadPaths => false;

    public bool ShouldWait(FlowchartJoinContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var arrivedSourceIds = context.LiveArrivals
            .Select(arrival => arrival.SourceNodeId)
            .ToHashSet(StringComparer.Ordinal);

        return context.InboundConnections
            .Where(inbound => !arrivedSourceIds.Contains(inbound.Source.NodeId))
            .Any(inbound => context.State.ActiveChildren.Any(child =>
                StringComparer.Ordinal.Equals(child.ExecutionScopeId, context.ExecutionScopeId) &&
                context.CanReachForward(child.NodeId, inbound.Source.NodeId)));
    }
}
