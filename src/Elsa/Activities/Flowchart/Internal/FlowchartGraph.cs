using System.Text.Json;
using Elsa.Activities.Flowchart.Exceptions;
using Elsa.Activities.Flowchart.Models;
using Elsa.Workflows.Runtime.Core.Models;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Activities.Flowchart.Internal;

public sealed class FlowchartGraph
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IReadOnlyDictionary<string, ExecutableNode> _nodesById;
    private readonly IReadOnlyCollection<FlowchartConnection> _connections;
    private readonly IReadOnlyDictionary<string, FlowchartNodeMetadata> _nodeMetadata;
    private readonly IReadOnlyDictionary<string, FlowchartConnectionMetadata> _connectionMetadata;
    private readonly string? _startNodeId;

    private FlowchartGraph(
        IReadOnlyList<ExecutableNode> children,
        IReadOnlyDictionary<string, ExecutableNode> nodesById,
        IReadOnlyCollection<FlowchartConnection> connections,
        string? startNodeId,
        IReadOnlyDictionary<string, FlowchartNodeMetadata> nodeMetadata,
        IReadOnlyDictionary<string, FlowchartConnectionMetadata> connectionMetadata)
    {
        Children = children;
        _nodesById = nodesById;
        _connections = connections;
        _startNodeId = startNodeId;
        _nodeMetadata = nodeMetadata;
        _connectionMetadata = connectionMetadata;
    }

    public IReadOnlyList<ExecutableNode> Children { get; }
    public IReadOnlyCollection<FlowchartConnection> Connections => _connections;

    public static FlowchartGraph From(ExecutableNode executableNode)
    {
        ArgumentNullException.ThrowIfNull(executableNode);

        var slot = executableNode.ChildSlots.FirstOrDefault(slot => StringComparer.Ordinal.Equals(slot.Name, FlowchartActivity.ActivitiesSlotName));
        if (slot is null)
            return new FlowchartGraph([], new Dictionary<string, ExecutableNode>(StringComparer.Ordinal), [], null, new Dictionary<string, FlowchartNodeMetadata>(), new Dictionary<string, FlowchartConnectionMetadata>());

        var children = slot.Activities.ToArray();
        var nodesById = children.ToDictionary(child => child.ExecutableNodeId, StringComparer.Ordinal);
        var structure = ReadStructure(executableNode);
        var connections = structure?.Connections ?? [];
        var startNodeId = NormalizeStartNodeId(structure?.StartNodeId);

        ValidateConnections(connections, nodesById);
        if (startNodeId is not null && !nodesById.ContainsKey(startNodeId))
            throw new FlowchartExecutionException($"Flowchart start node '{startNodeId}' does not exist in child slot '{FlowchartActivity.ActivitiesSlotName}'.");

        return new FlowchartGraph(
            children,
            nodesById,
            connections,
            startNodeId,
            structure?.NodeMetadata ?? new Dictionary<string, FlowchartNodeMetadata>(),
            structure?.ConnectionMetadata ?? new Dictionary<string, FlowchartConnectionMetadata>());
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

    public ExecutableNode GetRequiredNode(string executableNodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);

        if (_nodesById.TryGetValue(executableNodeId, out var node))
            return node;

        throw new FlowchartExecutionException($"Flowchart child executable node '{executableNodeId}' does not exist in child slot '{FlowchartActivity.ActivitiesSlotName}'.");
    }

    public IReadOnlyCollection<FlowchartConnection> SelectOutboundConnections(string sourceNodeId, IReadOnlyCollection<string> outcomeNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceNodeId);
        ArgumentNullException.ThrowIfNull(outcomeNames);

        if (!_nodesById.ContainsKey(sourceNodeId))
            throw new FlowchartExecutionException($"Completed child executable node '{sourceNodeId}' does not exist in child slot '{FlowchartActivity.ActivitiesSlotName}'.");

        var outcomes = NormalizeOutcomes(outcomeNames);
        return _connections
            .Where(connection => StringComparer.Ordinal.Equals(connection.Source.NodeId, sourceNodeId))
            .Where(connection => outcomes.Contains(connection.Source.Port))
            .ToArray();
    }

    public IReadOnlyCollection<FlowchartConnection> GetInboundConnections(string targetNodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetNodeId);
        return _connections
            .Where(connection => StringComparer.Ordinal.Equals(connection.Target.NodeId, targetNodeId))
            .ToArray();
    }

    public FlowchartConnection? FindConnection(string sourceNodeId, string targetNodeId, string? sourcePort = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetNodeId);

        var normalizedPort = sourcePort is null ? null : FlowchartEndpoint.NormalizePort(sourcePort);
        return _connections.FirstOrDefault(connection =>
            StringComparer.Ordinal.Equals(connection.Source.NodeId, sourceNodeId) &&
            StringComparer.Ordinal.Equals(connection.Target.NodeId, targetNodeId) &&
            (normalizedPort is null || StringComparer.Ordinal.Equals(connection.Source.Port, normalizedPort)));
    }

    public string GetConnectionId(FlowchartConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return connection.Id ?? $"{connection.Source.NodeId}:{connection.Source.Port}->{connection.Target.NodeId}";
    }

    public FlowchartNodeMetadata GetNodeMetadata(string nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        return _nodeMetadata.TryGetValue(nodeId, out var metadata) ? metadata : new FlowchartNodeMetadata();
    }

    public FlowchartConnectionMetadata GetConnectionMetadata(string connectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        return _connectionMetadata.TryGetValue(connectionId, out var metadata) ? metadata : new FlowchartConnectionMetadata();
    }

    public bool CanReach(string sourceNodeId, string targetNodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetNodeId);

        if (StringComparer.Ordinal.Equals(sourceNodeId, targetNodeId))
            return true;

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(sourceNodeId);
        visited.Add(sourceNodeId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var next in _connections
                         .Where(connection => StringComparer.Ordinal.Equals(connection.Source.NodeId, current))
                         .Select(connection => connection.Target.NodeId))
            {
                if (StringComparer.Ordinal.Equals(next, targetNodeId))
                    return true;

                if (visited.Add(next))
                    queue.Enqueue(next);
            }
        }

        return false;
    }

    private static FlowchartStructure? ReadStructure(ExecutableNode executableNode)
    {
        if (executableNode.Structure is null)
            return null;

        if (!StringComparer.Ordinal.Equals(executableNode.Structure.Kind, FlowchartActivity.StructureKind))
            throw new FlowchartExecutionException($"Flowchart executable node '{executableNode.ExecutableNodeId}' has unsupported structure kind '{executableNode.Structure.Kind}'.");

        if (!StringComparer.Ordinal.Equals(executableNode.Structure.SchemaVersion, FlowchartActivity.StructureSchemaVersion))
            throw new FlowchartExecutionException($"Flowchart executable node '{executableNode.ExecutableNodeId}' has unsupported structure schema version '{executableNode.Structure.SchemaVersion}'.");

        try
        {
            return executableNode.Structure.Payload.Deserialize<FlowchartStructure>(SerializerOptions)
                   ?? throw new FlowchartExecutionException($"Flowchart executable node '{executableNode.ExecutableNodeId}' structure resolved to null.");
        }
        catch (FlowchartExecutionException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            throw new FlowchartExecutionException($"Flowchart executable node '{executableNode.ExecutableNodeId}' structure is not a valid Flowchart structure payload.", exception);
        }
    }

    private static string? NormalizeStartNodeId(string? startNodeId)
    {
        if (string.IsNullOrWhiteSpace(startNodeId))
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
