using Elsa.Activities.Flowchart.Internal.Policies;
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
    IReadOnlyDictionary<string, IReadOnlyCollection<string>> predecessors,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? inboundSources = null,
    IReadOnlySet<string>? nonWaitingJoins = null)
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _inboundSources =
        inboundSources ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
    private readonly IReadOnlySet<string> _nonWaitingJoins = nonWaitingJoins ?? new HashSet<string>(StringComparer.Ordinal);

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
        // Each node run is scheduled by exactly one run: the node that just completed. So a pass is linked to the pass
        // before it, and a producer that can be reached again from itself has its earlier runs behind later consumers.
        // A merge node that runs once per arrival without a cycle is no exception: each of its runs schedules its own
        // successors, so no run of it lies behind another.
        return new(availability, cyclicBackEdge, !forward && !backward && !exclusive, exclusive)
        { EarlierProducerRunCanReachConsumer = IsReachable(producer, producer, requireEdge: true) };
    }

    /// <summary>
    /// How two children relate in time (spec 266, D-2/D-2a). Paths through the same port and paths through different
    /// ports both count as forks: any activity may select several outcomes (ADR 0045 §6, amendment of 2026-11-16).
    /// Branches from different trigger entries are exclusive. A child reachable from another runs after it, because
    /// every join waits for its live inbound paths, except a <c>merge</c> join, which runs once per arrival.
    /// </summary>
    public ActivityChildTimeRelation Relate(string first, string second)
    {
        if (!nodeIds.Contains(first) || !nodeIds.Contains(second))
            return ActivityChildTimeRelation.Unspecified;
        if (StringComparer.Ordinal.Equals(first, second))
            return SelfOverlaps(first) ? ActivityChildTimeRelation.Unordered : ActivityChildTimeRelation.FirstBeforeSecond;

        var forward = IsReachable(first, second);
        var backward = IsReachable(second, first);
        if (!forward && !backward)
            return AreExclusive(first, second) ? ActivityChildTimeRelation.MutuallyExclusive : ActivityChildTimeRelation.Unordered;

        // In a cycle each child runs after the other's earlier pass; take the direction a dominator gives.
        var firstEarlier = forward && (!backward || !Dominates(second, first));
        return firstEarlier
            ? OrderedThroughJoins(first, second) ? ActivityChildTimeRelation.FirstBeforeSecond : ActivityChildTimeRelation.Unordered
            : OrderedThroughJoins(second, first) ? ActivityChildTimeRelation.SecondBeforeFirst : ActivityChildTimeRelation.Unordered;
    }

    // A non-waiting join between the two that another arrival can reach without the earlier child lets the later
    // child run while the earlier one is still in flight.
    private bool OrderedThroughJoins(string earlier, string later) =>
        _nonWaitingJoins
            .Where(join => !StringComparer.Ordinal.Equals(join, earlier))
            .Where(join => IsReachable(earlier, join) && (StringComparer.Ordinal.Equals(join, later) || IsReachable(join, later)))
            .All(join => Dominates(earlier, join));

    // A non-waiting join runs once per arrival. Two arrivals can be in flight together when two of its inbound
    // connections come from one node (it may select several outcomes) or from two nodes that are neither ordered nor
    // exclusive. Every node reachable from such a join runs once per join run.
    private bool SelfOverlaps(string node) =>
        _nonWaitingJoins.Any(join =>
            (StringComparer.Ordinal.Equals(join, node) || IsReachable(join, node)) && ArrivalsCanOverlap(join));

    private bool ArrivalsCanOverlap(string join)
    {
        var sources = _inboundSources.GetValueOrDefault(join, []);
        if (sources.Count != sources.Distinct(StringComparer.Ordinal).Count())
            return true;
        for (var left = 0; left < sources.Count; left++)
        for (var right = left + 1; right < sources.Count; right++)
        {
            if (!IsReachable(sources[left], sources[right]) && !IsReachable(sources[right], sources[left]) &&
                !AreExclusive(sources[left], sources[right]))
                return true;
        }

        return false;
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
        var startNodeId = FlowchartGraph.NormalizeStartNodeId(structure.StartNodeId)
            ?? children.FirstOrDefault(id => !targetIds.Contains(id)) ?? children[0];
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
        var inboundSources = nodeIds.ToDictionary(
            id => id,
            id => (IReadOnlyList<string>)edges.Where(edge => StringComparer.Ordinal.Equals(edge.Target, id)).Select(edge => edge.Source).ToArray(),
            StringComparer.Ordinal);
        // FlowchartJoinCoordinator.ShouldWaitForTarget: only a merge node with several inbound connections does not wait.
        var nonWaitingJoins = nodeIds
            .Where(id => inboundSources[id].Count > 1 &&
                         structure.NodeMetadata.TryGetValue(id, out var metadata) &&
                         StringComparer.Ordinal.Equals(metadata?.PolicyKind, FlowchartPolicyKinds.Merge))
            .ToHashSet(StringComparer.Ordinal);
        return new FlowchartChildResultAnalysis(entryNodeIds, nodeIds, successors, predecessors, inboundSources, nonWaitingJoins);
    }

}
