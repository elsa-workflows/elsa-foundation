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
            .Select(ToScheduleCommand)
            .ToArray();
    }

    public static IReadOnlyCollection<FlowchartPolicyCommand> ScheduleAllOutbound(IFlowchartPolicyContext context) =>
        Outbound(context).Select(ToScheduleCommand).ToArray();

    public static IReadOnlyCollection<FlowchartPolicyCommand> ScheduleFirstMatchingOutbound(IFlowchartPolicyContext context)
    {
        var outcomes = NormalizeOutcomes(context.OutcomeNames);
        var connection = Outbound(context).FirstOrDefault(connection => outcomes.Contains(connection.Source.Port));
        return connection is null ? [] : [ToScheduleCommand(connection)];
    }

    private static IEnumerable<FlowchartConnection> Outbound(IFlowchartPolicyContext context) =>
        string.IsNullOrWhiteSpace(context.CurrentNodeId)
            ? []
            : context.Connections.Where(connection => StringComparer.Ordinal.Equals(connection.Source.NodeId, context.CurrentNodeId));

    private static FlowchartPolicyCommand ToScheduleCommand(FlowchartConnection connection) =>
        new(
            FlowchartPolicyCommandKind.ScheduleNode,
            nodeId: connection.Target.NodeId,
            connectionId: ConnectionId(connection));

    private static HashSet<string> NormalizeOutcomes(IReadOnlyCollection<string> outcomeNames) =>
        (outcomeNames.Count == 0 ? [FlowchartEndpoint.NormalizePort(null)] : outcomeNames.Select(FlowchartEndpoint.NormalizePort)).ToHashSet(StringComparer.Ordinal);

    private static string ConnectionId(FlowchartConnection connection) =>
        connection.Id ?? $"{connection.Source.NodeId}:{connection.Source.Port}->{connection.Target.NodeId}";
}
