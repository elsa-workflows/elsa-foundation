using Elsa.Activities.Flowchart.Models;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Flowchart.Internal;

/// <summary>
/// Flowchart-owned child-result analysis. A run starts at its selected entry: the start node or a direct child
/// without an incoming connection. Dominance therefore uses a virtual root above all those legal entries.
/// </summary>
internal sealed class FlowchartChildResultAnalysis(
    IReadOnlyCollection<string> entryNodeIds,
    IReadOnlyCollection<string> nodeIds,
    IReadOnlyDictionary<string, IReadOnlyCollection<string>> successors,
    IReadOnlyDictionary<string, IReadOnlyCollection<string>> predecessors)
{
    public ActivityChildResultFacts Assess(string producer, string consumer)
    {
        var sameChild = StringComparer.Ordinal.Equals(producer, consumer);
        var forward = IsReachable(producer, consumer);
        var backward = IsReachable(consumer, producer);
        var dominates = Dominates(producer, consumer);
        var exclusive = AreExclusive(producer, consumer);
        var cyclicBackEdge = sameChild
            ? IsReachable(producer, producer, requireEdge: true)
            : forward && backward && !dominates;
        var availability = sameChild
            ? ActivityChildResultAvailability.Unavailable
            : dominates
                ? ActivityChildResultAvailability.Definite
                : forward && entryNodeIds.Any(entry => IsReachable(entry, producer))
                    ? ActivityChildResultAvailability.Conditional
                    : ActivityChildResultAvailability.Unavailable;

        // Preserve the existing concurrent-write relation; branch write/merge policy stays with its validator.
        return new(availability, cyclicBackEdge, !forward && !backward && !exclusive, exclusive);
    }

    private bool IsReachable(string source, string target, bool requireEdge = false)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(source);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current))
                continue;

            foreach (var next in successors.GetValueOrDefault(current, []))
            {
                if (StringComparer.Ordinal.Equals(next, target) && (!StringComparer.Ordinal.Equals(source, target) || requireEdge))
                    return true;
                pending.Push(next);
            }
        }

        return !requireEdge && StringComparer.Ordinal.Equals(source, target);
    }

    /// <summary>Whether every path from every entry to the node passes the candidate.</summary>
    private bool Dominates(string candidate, string node)
    {
        var entries = entryNodeIds.ToHashSet(StringComparer.Ordinal);
        var reachable = nodeIds.Where(id => entries.Any(entry => IsReachable(entry, id))).ToHashSet(StringComparer.Ordinal);
        if (!reachable.Contains(candidate) || !reachable.Contains(node))
            return false;

        var dominators = reachable.ToDictionary(
            id => id,
            id => entries.Contains(id)
                ? new HashSet<string>([id], StringComparer.Ordinal)
                : new HashSet<string>(reachable, StringComparer.Ordinal),
            StringComparer.Ordinal);

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var current in reachable.Where(id => !entries.Contains(id)))
            {
                var incoming = predecessors.GetValueOrDefault(current, []).Where(reachable.Contains).ToArray();
                var next = incoming.Length == 0
                    ? []
                    : new HashSet<string>(dominators[incoming[0]], StringComparer.Ordinal);
                foreach (var predecessor in incoming.Skip(1))
                    next.IntersectWith(dominators[predecessor]);
                next.Add(current);
                if (dominators[current].SetEquals(next))
                    continue;
                dominators[current] = next;
                changed = true;
            }
        }

        return dominators[node].Contains(candidate);
    }

    /// <summary>Whether each node has a legal entry and no entry can reach both nodes.</summary>
    private bool AreExclusive(string left, string right)
    {
        var leftEntries = EntriesReaching(left);
        var rightEntries = EntriesReaching(right);
        return leftEntries.Count > 0 && rightEntries.Count > 0 && !leftEntries.Overlaps(rightEntries);
    }

    private HashSet<string> EntriesReaching(string node) =>
        entryNodeIds.Where(entry => IsReachable(entry, node)).ToHashSet(StringComparer.Ordinal);

    public static FlowchartChildResultAnalysis? TryCreate(FlowchartAuthoredStructure structure)
    {
        var children = structure.Activities.Select(child => child.NodeId).ToArray();
        var childNodeIds = children.ToHashSet(StringComparer.Ordinal);
        if (children.Length == 0 || children.Any(string.IsNullOrWhiteSpace) || childNodeIds.Count != children.Length)
            return null;

        var edges = new List<(string Source, string Target)>();
        foreach (var connection in structure.Connections)
        {
            if (connection?.Source is null || connection.Target is null ||
                !childNodeIds.Contains(connection.Source.NodeId) || !childNodeIds.Contains(connection.Target.NodeId))
                return null;
            edges.Add((connection.Source.NodeId, connection.Target.NodeId));
        }

        var targetIds = edges.Select(edge => edge.Target).ToHashSet(StringComparer.Ordinal);
        // Match FlowchartGraph.SelectStartNode, including the first-child fallback for a cycle.
        // Every child without inbound connections remains a legal trigger entry as well.
        var startNodeId = structure.StartNodeId ?? children.FirstOrDefault(id => !targetIds.Contains(id)) ?? children[0];
        if (!childNodeIds.Contains(startNodeId))
            return null;
        var entryNodeIds = children
            .Where(id => !targetIds.Contains(id))
            .Prepend(startNodeId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var nodeIds = childNodeIds;
        var successors = nodeIds.ToDictionary(
            id => id,
            id => (IReadOnlyCollection<string>)edges.Where(edge => StringComparer.Ordinal.Equals(edge.Source, id)).Select(edge => edge.Target).Distinct(StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);
        var predecessors = nodeIds.ToDictionary(
            id => id,
            id => (IReadOnlyCollection<string>)edges.Where(edge => StringComparer.Ordinal.Equals(edge.Target, id)).Select(edge => edge.Source).Distinct(StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);
        return new FlowchartChildResultAnalysis(entryNodeIds, nodeIds, successors, predecessors);
    }

}
