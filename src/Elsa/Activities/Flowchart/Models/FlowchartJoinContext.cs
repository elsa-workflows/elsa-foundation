namespace Elsa.Activities.Flowchart.Models;

/// <summary>
/// Everything a join decision is allowed to look at: the execution state, the target's inbound connections,
/// the <c>(node, scope, iteration)</c> partition key the arrivals are matched on, and a forward-projection
/// reachability oracle.
/// <para>
/// The oracle is a parameter rather than a fact of the model on purpose. It is what the reachability join
/// needs and what the dead-path join does not — ADR 0064's whole claim is that a join can be decided from
/// arrivals the target already holds, so the policy that ignores this argument is the one making the point.
/// </para>
/// </summary>
/// <param name="State">The Flowchart execution state as of this decision.</param>
/// <param name="InboundConnections">Every connection that targets <paramref name="TargetNodeId"/>. Always more
/// than one — a target with a single inbound is not a join and never waits.</param>
/// <param name="TargetNodeId">The node being joined.</param>
/// <param name="ExecutionScopeId">The execution scope the arrivals are partitioned by.</param>
/// <param name="IterationKey">The loop-iteration key the arrivals are partitioned by, or <c>null</c> outside a loop.</param>
/// <param name="CanReachForward">Whether the second node is reachable from the first over the graph's forward
/// (acyclic) projection — backward, loop-closing edges excluded.</param>
public sealed record FlowchartJoinContext(
    FlowchartExecutionState State,
    IReadOnlyCollection<FlowchartConnection> InboundConnections,
    string TargetNodeId,
    string ExecutionScopeId,
    string? IterationKey,
    Func<string, string, bool> CanReachForward)
{
    /// <summary>Arrivals at this key that carried a live token.</summary>
    public IEnumerable<FlowchartArrival> LiveArrivals =>
        Arrivals.Where(arrival => arrival.Status == FlowchartArrivalStatus.Arrived);

    /// <summary>Arrivals at this key that have not been consumed yet, live and dead alike.</summary>
    public IEnumerable<FlowchartArrival> Arrivals =>
        State.Arrivals.Where(arrival =>
            arrival.Status != FlowchartArrivalStatus.Consumed &&
            StringComparer.Ordinal.Equals(arrival.TargetNodeId, TargetNodeId) &&
            StringComparer.Ordinal.Equals(arrival.ExecutionScopeId, ExecutionScopeId) &&
            StringComparer.Ordinal.Equals(arrival.IterationKey, IterationKey));
}
