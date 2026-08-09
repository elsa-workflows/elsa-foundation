using Elsa.Activities.Flowchart.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using static Elsa.Activities.Flowchart.Internal.FlowchartScheduler;
using static Elsa.Activities.Flowchart.Internal.FlowchartStateMutator;

namespace Elsa.Activities.Flowchart.Internal;

/// <summary>
/// Join and arrival bookkeeping for the Flowchart engine — the <c>FlowchartJoinHandler</c> named in #275.
/// Decides whether an inbound branch must wait at an implicit join, releases waiting joins once every
/// required branch has arrived, and fires the join (or a plain continuation) by consuming arrivals,
/// completing the waiting paths, and scheduling the target node. Mechanical extraction of the former engine
/// helpers; behavior and ordering are unchanged.
/// </summary>
public sealed class FlowchartJoinCoordinator
{
    public FlowchartExecutionState ReleaseReadyWaitingJoins(IRuntimeActivityExecutionContext context, FlowchartExecutionState state, FlowchartGraph graph)
    {
        var groups = state.ExecutionPaths
            .Where(path => path.Status == ExecutionPathStatus.Waiting && path.CurrentNodeId is not null)
            .GroupBy(path => (path.CurrentNodeId!, path.ExecutionScopeId, path.IterationKey))
            .ToArray();

        foreach (var group in groups)
        {
            if (ShouldWaitForTarget(state, graph, group.Key.Item1, group.Key.ExecutionScopeId, group.Key.IterationKey))
                continue;

            var arrival = MatchingArrivals(state, group.Key.Item1, group.Key.ExecutionScopeId, group.Key.IterationKey).FirstOrDefault();
            if (arrival is null)
                continue;

            state = FireJoinOrContinuation(
                context,
                state,
                graph,
                group.Key.Item1,
                group.Key.ExecutionScopeId,
                group.Key.IterationKey,
                arrival.ProducingActivityExecutionId,
                arrival.ConnectionId);
        }

        return state;
    }

    public FlowchartExecutionState FireJoinOrContinuation(
        IRuntimeActivityExecutionContext context,
        FlowchartExecutionState state,
        FlowchartGraph graph,
        string targetNodeId,
        string executionScopeId,
        string? iterationKey,
        string schedulingActivityExecutionId,
        string? connectionId)
    {
        var arrivals = MatchingArrivals(state, targetNodeId, executionScopeId, iterationKey).ToArray();
        foreach (var arrival in arrivals)
        {
            state = UpdateArrival(state, arrival with { Status = FlowchartArrivalStatus.Consumed });
            var arrivalPath = state.ExecutionPaths.FirstOrDefault(path => StringComparer.Ordinal.Equals(path.ExecutionPathId, arrival.ExecutionPathId));
            if (arrivalPath is not null)
                state = UpdatePath(state, arrivalPath with { Status = ExecutionPathStatus.Completed });
        }

        foreach (var waitingPath in state.ExecutionPaths.Where(path =>
                     path.Status == ExecutionPathStatus.Waiting &&
                     StringComparer.Ordinal.Equals(path.CurrentNodeId, targetNodeId) &&
                     StringComparer.Ordinal.Equals(path.ExecutionScopeId, executionScopeId) &&
                     StringComparer.Ordinal.Equals(path.IterationKey, iterationKey)).ToArray())
            state = UpdatePath(state, waitingPath with { Status = ExecutionPathStatus.Completed });

        var scheduledPath = NewPath(state, null, executionScopeId, targetNodeId, connectionId, schedulingActivityExecutionId, ExecutionPathStatus.Active, iterationKey);
        state = AddPath(state, scheduledPath);
        state = FlowchartDiagnosticAccumulator.Add(state, arrivals.Length > 1 ? FlowchartDiagnosticKind.Joined : FlowchartDiagnosticKind.Scheduled, targetNodeId, connectionId, scheduledPath.ExecutionPathId, executionScopeId, arrivals.Length > 1
            ? $"Implicit join '{targetNodeId}' fired after {arrivals.Length} active arrival(s)."
            : $"Flowchart scheduled node '{targetNodeId}'.");

        return ScheduleNode(context, state, targetNodeId, scheduledPath.ExecutionPathId, executionScopeId, scheduledPath.IterationKey, schedulingActivityExecutionId, arrivals.Length > 1 ? "join" : "continuation");
    }

    public static bool ShouldWaitForTarget(FlowchartExecutionState state, FlowchartGraph graph, string targetNodeId, string executionScopeId, string? iterationKey)
    {
        var policyKind = graph.GetNodeMetadata(targetNodeId).PolicyKind;
        if (StringComparer.Ordinal.Equals(policyKind, Internal.Policies.FlowchartPolicyKinds.Merge))
            return false;

        return ShouldWaitForImplicitJoin(state, graph, targetNodeId, executionScopeId, iterationKey);
    }

    public static IEnumerable<FlowchartArrival> MatchingArrivals(FlowchartExecutionState state, string targetNodeId, string executionScopeId, string? iterationKey) =>
        state.Arrivals.Where(arrival =>
            arrival.Status == FlowchartArrivalStatus.Arrived &&
            StringComparer.Ordinal.Equals(arrival.TargetNodeId, targetNodeId) &&
            StringComparer.Ordinal.Equals(arrival.ExecutionScopeId, executionScopeId) &&
            StringComparer.Ordinal.Equals(arrival.IterationKey, iterationKey));

    /// <summary>
    /// The reachability join predicate: wait while some live child in the same execution scope can still
    /// reach an inbound source that has not arrived. The walk runs over the graph's <b>forward projection</b>
    /// (<see cref="FlowchartGraph.CanReachForward"/>), not the whole connection set — ADR 0064 WU-1. Walking
    /// backward edges too makes every node of a loop reach every other, so the predicate answered "are these
    /// in the same loop" and was nearly always true inside one; the only thing that kept it from deadlocking
    /// was the <see cref="FlowchartExecutionState.ActiveChildren"/> filter on <paramref name="executionScopeId"/>,
    /// which left join accounting entangled with the scope model. Over the forward projection the question is
    /// the one a join actually asks — can a live token still arrive <em>this iteration</em>.
    /// </summary>
    private static bool ShouldWaitForImplicitJoin(FlowchartExecutionState state, FlowchartGraph graph, string targetNodeId, string executionScopeId, string? iterationKey)
    {
        var inboundConnections = graph.GetInboundConnections(targetNodeId);
        if (inboundConnections.Count <= 1)
            return false;

        var arrivedSourceIds = MatchingArrivals(state, targetNodeId, executionScopeId, iterationKey)
            .Select(arrival => arrival.SourceNodeId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var inboundConnection in inboundConnections)
        {
            if (arrivedSourceIds.Contains(inboundConnection.Source.NodeId))
                continue;

            if (state.ActiveChildren.Any(child => StringComparer.Ordinal.Equals(child.ExecutionScopeId, executionScopeId) && graph.CanReachForward(child.NodeId, inboundConnection.Source.NodeId)))
                return true;
        }

        return false;
    }
}
