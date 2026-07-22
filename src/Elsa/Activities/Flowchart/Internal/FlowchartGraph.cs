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
    // ADR 0047 D3: publish-time routing indexes. The outcome→successor relation is precomputed at graph
    // materialization so per-completion routing is a dictionary lookup on the source node id rather than a linear
    // scan of every connection. Each group preserves connection declaration order (the scan's order), so the
    // index is byte-for-byte equivalent to the retained SelectOutboundConnectionsByScan reference walk.
    private readonly IReadOnlyDictionary<string, IReadOnlyList<FlowchartConnection>> _outboundBySource;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<FlowchartConnection>> _inboundByTarget;
    private readonly IReadOnlyDictionary<string, FlowchartNodeMetadata> _nodeMetadata;
    private readonly IReadOnlyDictionary<string, FlowchartConnectionMetadata> _connectionMetadata;
    private readonly string? _startNodeId;
    private readonly IReadOnlySet<(string Source, string Target)> _backwardEdges;

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
        _outboundBySource = BuildConnectionIndex(connections, connection => connection.Source.NodeId);
        _inboundByTarget = BuildConnectionIndex(connections, connection => connection.Target.NodeId);
        _startNodeId = startNodeId;
        _nodeMetadata = nodeMetadata;
        _connectionMetadata = connectionMetadata;
        _backwardEdges = ComputeBackwardEdges();
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<FlowchartConnection>> BuildConnectionIndex(
        IReadOnlyCollection<FlowchartConnection> connections,
        Func<FlowchartConnection, string> keySelector)
    {
        var index = new Dictionary<string, List<FlowchartConnection>>(StringComparer.Ordinal);
        foreach (var connection in connections)
        {
            var key = keySelector(connection);
            if (!index.TryGetValue(key, out var list))
            {
                list = [];
                index[key] = list;
            }

            list.Add(connection);
        }

        return index.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<FlowchartConnection>)pair.Value,
            StringComparer.Ordinal);
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
        var targetIds = SelectOutboundConnections(completedChildExecutableNodeId, outcomeNames)
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

    /// <summary>
    /// ADR 0047 D3: routes a completed child's outcomes to its outbound connections by an O(1) lookup on the
    /// precomputed <c>source node id → outbound connections</c> index, then filtering to the matching outcome
    /// ports. Order is the connection declaration order (the index preserves it), identical to
    /// <see cref="SelectOutboundConnectionsByScan"/>.
    /// </summary>
    public IReadOnlyCollection<FlowchartConnection> SelectOutboundConnections(string sourceNodeId, IReadOnlyCollection<string> outcomeNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceNodeId);
        ArgumentNullException.ThrowIfNull(outcomeNames);

        if (!_nodesById.ContainsKey(sourceNodeId))
            throw new FlowchartExecutionException($"Completed child executable node '{sourceNodeId}' does not exist in child slot '{FlowchartActivity.ActivitiesSlotName}'.");

        var outcomes = NormalizeOutcomes(outcomeNames);
        if (!_outboundBySource.TryGetValue(sourceNodeId, out var outbound))
            return [];

        return outbound
            .Where(connection => outcomes.Contains(connection.Source.Port))
            .ToArray();
    }

    /// <summary>
    /// ADR 0047 D3 differential-guardrail reference: the pre-D3 linear scan over the full connection set,
    /// retained callable so the index-backed <see cref="SelectOutboundConnections"/> can be proven byte-for-byte
    /// equivalent (same connections, same order) across a representative corpus. Not used on the hot path.
    /// </summary>
    public IReadOnlyCollection<FlowchartConnection> SelectOutboundConnectionsByScan(string sourceNodeId, IReadOnlyCollection<string> outcomeNames)
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
        return _inboundByTarget.TryGetValue(targetNodeId, out var inbound)
            ? inbound.ToArray()
            : [];
    }

    /// <summary>
    /// ADR 0047 D3 differential-guardrail reference: the pre-D3 linear scan for inbound connections, retained
    /// callable so <see cref="GetInboundConnections"/>'s index path can be proven equivalent.
    /// </summary>
    public IReadOnlyCollection<FlowchartConnection> GetInboundConnectionsByScan(string targetNodeId)
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

    public FlowchartConnection? FindConnectionById(string connectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        return _connections.FirstOrDefault(connection => StringComparer.Ordinal.Equals(GetConnectionId(connection), connectionId));
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
            var outbound = _outboundBySource.TryGetValue(current, out var connections)
                ? connections
                : [];
            foreach (var next in outbound.Select(connection => connection.Target.NodeId))
            {
                if (StringComparer.Ordinal.Equals(next, targetNodeId))
                    return true;

                if (visited.Add(next))
                    queue.Enqueue(next);
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the connection <paramref name="sourceNodeId"/> → <paramref name="targetNodeId"/> is a backward
    /// (loop-closing) edge: a token that traverses it opens a fresh loop-iteration scope/key rather than
    /// inheriting the emitting path's (see <see cref="Internal.FlowchartScopeResolver.ResolveTargetScope"/>).
    /// The set is precomputed once at construction (<see cref="ComputeBackwardEdges"/>) as the standard
    /// depth-first back-edge set, so it marks exactly the loop-closing edge of each cycle — never a
    /// forward/cross edge — and is a deterministic function of the node/connection/start-node sets.
    /// </summary>
    public bool IsBackwardEdge(string sourceNodeId, string targetNodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetNodeId);
        return _backwardEdges.Contains((sourceNodeId, targetNodeId));
    }

    /// <summary>
    /// Classifies the graph's backward (loop-closing) edges once at construction. A backward edge is the
    /// standard compiler back edge: during a depth-first traversal from the start node (then any remaining
    /// unvisited nodes), an edge <c>u → v</c> is backward iff <c>v</c> is GRAY — on the current DFS stack, an
    /// ancestor of <c>u</c> — when the edge is examined. This marks exactly the loop-closing edge of each loop
    /// and never a forward/cross edge of the cycle, unlike the naive "target can reach source" rule, which
    /// marks every edge of a cycle as backward (so a fork/join inside a loop body would split its branches
    /// into divergent iteration scopes that never reconverge). Roots are the start node first, then every
    /// remaining node ordinal-sorted; each node's outbound connections are ordinal-sorted by connection id, so
    /// the result is stable regardless of authoring order. Mirrors <c>BpmnGraph.ComputeBackwardFlowIds</c>
    /// (spec 122).
    /// </summary>
    private IReadOnlySet<(string Source, string Target)> ComputeBackwardEdges()
    {
        const int gray = 1;
        const int black = 2;

        var backward = new HashSet<(string Source, string Target)>();
        var color = new Dictionary<string, int>(StringComparer.Ordinal);
        var outboundBySource = _connections.ToLookup(connection => connection.Source.NodeId, StringComparer.Ordinal);

        var roots = (SelectStartNode() is { } startNode ? new[] { startNode.ExecutableNodeId } : [])
            .Concat(_nodesById.Keys.OrderBy(id => id, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal);

        foreach (var root in roots)
            Visit(root);

        return backward;

        void Visit(string nodeId)
        {
            color[nodeId] = gray;

            foreach (var connection in outboundBySource[nodeId].OrderBy(GetConnectionId, StringComparer.Ordinal))
            {
                var target = connection.Target.NodeId;
                var known = color.TryGetValue(target, out var state) ? state : 0;
                if (known == gray)
                    backward.Add((nodeId, target)); // target is on the current DFS stack — a loop-closing edge.
                else if (known != black)
                    Visit(target);
            }

            color[nodeId] = black;
        }
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
