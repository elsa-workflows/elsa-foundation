using System.Text.Json;
using Elsa.Activities.Flowchart.Exceptions;
using Elsa.Activities.Flowchart.Models;
using Elsa.Workflows.Runtime.Core.Models;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Activities.Flowchart.Internal;

internal sealed class FlowchartGraph
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IReadOnlyDictionary<string, ExecutableNode> _nodesById;
    private readonly IReadOnlyCollection<FlowchartConnection> _connections;
    private readonly string? _startNodeId;

    private FlowchartGraph(
        IReadOnlyList<ExecutableNode> children,
        IReadOnlyDictionary<string, ExecutableNode> nodesById,
        IReadOnlyCollection<FlowchartConnection> connections,
        string? startNodeId)
    {
        Children = children;
        _nodesById = nodesById;
        _connections = connections;
        _startNodeId = startNodeId;
    }

    public IReadOnlyList<ExecutableNode> Children { get; }

    public static FlowchartGraph From(ExecutableNode executableNode)
    {
        ArgumentNullException.ThrowIfNull(executableNode);

        var slot = executableNode.ChildSlots.FirstOrDefault(slot => StringComparer.Ordinal.Equals(slot.Name, FlowchartActivity.ActivitiesSlotName));
        if (slot is null)
            return new FlowchartGraph([], new Dictionary<string, ExecutableNode>(StringComparer.Ordinal), [], null);

        var children = slot.Activities.ToArray();
        var nodesById = children.ToDictionary(child => child.ExecutableNodeId, StringComparer.Ordinal);
        var connections = ReadConnections(slot);
        var startNodeId = ReadStartNodeId(slot);

        ValidateConnections(connections, nodesById);
        if (startNodeId is not null && !nodesById.ContainsKey(startNodeId))
            throw new FlowchartExecutionException($"Flowchart start node '{startNodeId}' does not exist in child slot '{FlowchartActivity.ActivitiesSlotName}'.");

        return new FlowchartGraph(children, nodesById, connections, startNodeId);
    }

    public ExecutableNode? SelectStartNode()
    {
        if (Children.Count == 0)
            return null;

        if (_startNodeId is not null)
            return _nodesById[_startNodeId];

        var nodesWithInbound = _connections
            .Select(connection => connection.Target.NodeId)
            .ToHashSet(StringComparer.Ordinal);

        return Children.FirstOrDefault(child => !nodesWithInbound.Contains(child.ExecutableNodeId)) ?? Children[0];
    }

    public IReadOnlyCollection<ExecutableNode> SelectTargets(string completedChildExecutableNodeId, IReadOnlyCollection<string> outcomeNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(completedChildExecutableNodeId);
        ArgumentNullException.ThrowIfNull(outcomeNames);

        if (!_nodesById.ContainsKey(completedChildExecutableNodeId))
            throw new FlowchartExecutionException($"Completed child executable node '{completedChildExecutableNodeId}' does not exist in child slot '{FlowchartActivity.ActivitiesSlotName}'.");

        var outcomes = NormalizeOutcomes(outcomeNames);
        var targetIds = _connections
            .Where(connection => StringComparer.Ordinal.Equals(connection.Source.NodeId, completedChildExecutableNodeId))
            .Where(connection => outcomes.Contains(connection.Source.Port))
            .Select(connection => connection.Target.NodeId)
            .ToArray();

        if (targetIds.Distinct(StringComparer.Ordinal).Count() != targetIds.Length)
            throw new FlowchartExecutionException($"Flowchart completion for child executable node '{completedChildExecutableNodeId}' contains duplicate connection targets.");

        return targetIds.Select(targetId => _nodesById[targetId]).ToArray();
    }

    private static IReadOnlyCollection<FlowchartConnection> ReadConnections(ExecutableChildSlot slot)
    {
        if (!slot.Metadata.TryGetValue(FlowchartActivity.ConnectionsMetadataKey, out var serializedConnections) || string.IsNullOrWhiteSpace(serializedConnections))
            return [];

        try
        {
            return JsonSerializer.Deserialize<FlowchartConnection[]>(serializedConnections, SerializerOptions)
                   ?? throw new FlowchartExecutionException($"Flowchart metadata key '{FlowchartActivity.ConnectionsMetadataKey}' resolved to null.");
        }
        catch (FlowchartExecutionException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            throw new FlowchartExecutionException($"Flowchart metadata key '{FlowchartActivity.ConnectionsMetadataKey}' is not a valid connection array.", exception);
        }
    }

    private static string? ReadStartNodeId(ExecutableChildSlot slot)
    {
        if (!slot.Metadata.TryGetValue(FlowchartActivity.StartNodeIdMetadataKey, out var startNodeId) || string.IsNullOrWhiteSpace(startNodeId))
            return null;

        return startNodeId.Trim();
    }

    private static void ValidateConnections(
        IReadOnlyCollection<FlowchartConnection> connections,
        IReadOnlyDictionary<string, ExecutableNode> nodesById)
    {
        foreach (var connection in connections)
        {
            if (!nodesById.ContainsKey(connection.Source.NodeId))
                throw new FlowchartExecutionException($"Flowchart connection source node '{connection.Source.NodeId}' does not exist in child slot '{FlowchartActivity.ActivitiesSlotName}'.");

            if (!nodesById.ContainsKey(connection.Target.NodeId))
                throw new FlowchartExecutionException($"Flowchart connection target node '{connection.Target.NodeId}' does not exist in child slot '{FlowchartActivity.ActivitiesSlotName}'.");
        }
    }

    private static HashSet<string> NormalizeOutcomes(IReadOnlyCollection<string> outcomeNames)
    {
        var normalized = (outcomeNames.Count == 0 ? [FlowchartEndpoint.NormalizePort(null)] : outcomeNames.Select(FlowchartEndpoint.NormalizePort)).ToArray();
        if (normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length)
            throw new FlowchartExecutionException("Flowchart child completion outcomes cannot contain duplicates after normalization.");

        return normalized.ToHashSet(StringComparer.Ordinal);
    }
}
