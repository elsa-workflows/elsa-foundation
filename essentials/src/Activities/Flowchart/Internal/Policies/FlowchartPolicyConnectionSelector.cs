using Elsa.Activities.Flowchart.Contracts;
using Elsa.Activities.Flowchart.Models;

namespace Elsa.Activities.Flowchart.Internal.Policies;

internal static class FlowchartPolicyConnectionSelector
{
    public static IReadOnlyCollection<FlowchartPolicyCommand> ScheduleMatchingOutbound(IFlowchartPolicyContext context)
    {
        var outcomes = NormalizeOutcomes(context.OutcomeNames);
        return Outbound(context)
            .Where(connection => outcomes.Contains(connection.Source.Port))
            .DistinctBy(connection => connection.Target.NodeId, StringComparer.Ordinal)
            .Select(ToScheduleCommand)
            .ToArray();
    }

    public static IReadOnlyCollection<FlowchartPolicyCommand> ScheduleAllOutbound(IFlowchartPolicyContext context) =>
        Outbound(context).DistinctBy(connection => connection.Target.NodeId, StringComparer.Ordinal).Select(ToScheduleCommand).ToArray();

    private static IEnumerable<FlowchartConnection> Outbound(IFlowchartPolicyContext context) =>
        string.IsNullOrWhiteSpace(context.CurrentNodeId)
            ? []
            : context.Connections.Where(connection => StringComparer.Ordinal.Equals(connection.Source.NodeId, context.CurrentNodeId));

    // One command per distinct target: two taken connections that lead to the same node are two answers from one
    // source, and a join answers per source node, so the node runs once (FR-022), exactly as on the default routing
    // path. The other connections to that target are not untaken either: the join coordinator does not emit a dead
    // arrival over a connection whose target this completion already reached.
    private static FlowchartPolicyCommand ToScheduleCommand(FlowchartConnection connection) =>
        new(
            FlowchartPolicyCommandKind.ScheduleNode,
            nodeId: connection.Target.NodeId,
            connectionId: ConnectionId(connection));

    // An empty set stays empty: an explicitly empty selection takes no connection (ADR 0045 §6).
    private static HashSet<string> NormalizeOutcomes(IReadOnlyCollection<string> outcomeNames) =>
        outcomeNames.Select(FlowchartEndpoint.NormalizePort).ToHashSet(StringComparer.Ordinal);

    private static string ConnectionId(FlowchartConnection connection) =>
        connection.Id ?? $"{connection.Source.NodeId}:{connection.Source.Port}->{connection.Target.NodeId}";
}
