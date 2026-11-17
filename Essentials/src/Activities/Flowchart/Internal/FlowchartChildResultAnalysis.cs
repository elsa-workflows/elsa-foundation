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
    IReadOnlySet<string>? nonWaitingJoins = null,
    IReadOnlyDictionary<string, IReadOnlyCollection<string>>? forkBranches = null,
    IReadOnlySet<string>? raceJoins = null)
{
    private readonly IReadOnlySet<string> _raceJoins = raceJoins ?? new HashSet<string>(StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> _forkBranches =
        forkBranches ?? new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal);
    private readonly Dictionary<(string, string, bool), bool> _reachable = [];
    private Dictionary<string, HashSet<string>>? _dominators;
    private Dictionary<string, HashSet<string>>? _forkCycleRuns;

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
        lock (_reachable)
            return RelateCore(first, second);
    }

    private ActivityChildTimeRelation RelateCore(string first, string second)
    {
        if (!nodeIds.Contains(first) || !nodeIds.Contains(second))
            return ActivityChildTimeRelation.Unspecified;
        if (StringComparer.Ordinal.Equals(first, second))
            return SelfOverlaps(first) ? ActivityChildTimeRelation.Unordered : ActivityChildTimeRelation.FirstBeforeSecond;

        var forward = IsReachable(first, second);
        var backward = IsReachable(second, first);
        if (!forward && !backward)
            return AreExclusive(first, second) ? ActivityChildTimeRelation.MutuallyExclusive : ActivityChildTimeRelation.Unordered;

        // Two nodes that reach each other around a cycle: what counts is whether each reaches the other within one pass,
        // without going back through a node that dominates it (the way into the next pass). Both: one sequential path.
        // One: that one runs first. Neither: they sit on branches of one pass that run side by side.
        if (forward && backward)
        {
            var firstToSecond = ReachesWithinPass(first, second);
            var secondToFirst = ReachesWithinPass(second, first);
            if (!firstToSecond && !secondToFirst)
                return ActivityChildTimeRelation.Unordered;
            // Around a cycle each run of one is followed by a run of the other: declare both orders, so a reader checks
            // that both sides are done (see ActivityChildTimeRelation).
            var (earlierNode, laterNode) = firstToSecond ? (first, second) : (second, first);
            return OrderedThroughJoins(earlierNode, laterNode) && OrderedThroughJoins(laterNode, earlierNode) &&
                   !OverlapsALaterPass(earlierNode, laterNode) && !OverlapsALaterPass(laterNode, earlierNode)
                ? ActivityChildTimeRelation.FirstBeforeSecond
                : ActivityChildTimeRelation.Unordered;
        }
        var firstEarlier = forward && (!backward || Dominates(first, second));
        var (earlier, later) = firstEarlier ? (first, second) : (second, first);
        if (!OrderedThroughJoins(earlier, later) || OverlapsALaterPass(earlier, later))
            return ActivityChildTimeRelation.Unordered;
        return firstEarlier ? ActivityChildTimeRelation.FirstBeforeSecond : ActivityChildTimeRelation.SecondBeforeFirst;
    }

    // A branch that a fork on a cycle sends off the cycle can still run while the next pass of that cycle runs.
    private bool OverlapsALaterPass(string earlier, string later) =>
        ForkCycleRuns().TryGetValue(later, out var forks) &&
        forks.Any(fork => IsReachable(earlier, fork) && IsReachable(fork, earlier));

    // For each node on a branch that a fork on a cycle sends off the cycle, the forks that start a run of it on every
    // pass. Every node with several distinct successors is a fork, whatever their ports (D-2a as amended).
    private Dictionary<string, HashSet<string>> ForkCycleRuns()
    {
        if (_forkCycleRuns is not null)
            return _forkCycleRuns;
        var runs = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (fork, branches) in _forkBranches)
        {
            if (!IsReachable(fork, fork, requireEdge: true))
                continue;
            foreach (var branch in branches.Where(branch => !IsReachable(branch, fork)))
            foreach (var node in nodeIds.Where(node => StringComparer.Ordinal.Equals(node, branch) || IsReachable(branch, node)))
            {
                if (!runs.TryGetValue(node, out var forks))
                    runs[node] = forks = new HashSet<string>(StringComparer.Ordinal);
                forks.Add(fork);
            }
        }

        return _forkCycleRuns = runs;
    }

    // A non-waiting join between the two that another arrival can reach without the earlier child lets the later
    // child run while the earlier one is still in flight.
    private bool OrderedThroughJoins(string earlier, string later) =>
        _nonWaitingJoins.Concat(_raceJoins)
            .Where(join => !StringComparer.Ordinal.Equals(join, earlier))
            .Where(join => ReachesWithinPass(earlier, join) && (StringComparer.Ordinal.Equals(join, later) || ReachesWithinPass(join, later)))
            .All(join => Dominates(earlier, join) || !OtherArrivalCanOverlap(join, earlier));

    // Another arrival at the join can run it while the earlier child is in flight when it comes from a source that
    // neither follows the earlier child nor excludes it.
    private bool OtherArrivalCanOverlap(string join, string earlier) =>
        _inboundSources.GetValueOrDefault(join, []).Any(source =>
            !StringComparer.Ordinal.Equals(source, earlier) &&
            !ReachesWithinPass(earlier, source) &&
            !AreExclusive(source, earlier));

    // A non-waiting join runs once per arrival. Two arrivals can be in flight together when they come from two nodes
    // that are neither ordered nor exclusive. Every node reachable from such a join runs once per join run.
    private bool SelfOverlaps(string node) =>
        ForkCycleRuns().ContainsKey(node) ||
        _nonWaitingJoins.Any(join =>
            (StringComparer.Ordinal.Equals(join, node) || IsReachable(join, node)) && ArrivalsCanOverlap(join));

    private bool ArrivalsCanOverlap(string join)
    {
        // Two connections from one source are one arrival: a completion schedules each target once (spec 1208 FR-022).
        var sources = _inboundSources.GetValueOrDefault(join, []).Distinct(StringComparer.Ordinal).ToArray();
        for (var left = 0; left < sources.Length; left++)
        for (var right = left + 1; right < sources.Length; right++)
        {
            // Two arrivals each run the join. They are in flight together unless they exclude each other, or exactly one of
            // them is reached from the join within one pass: that one is the way back of a loop and only comes after the
            // join's run for the other.
            if (ReachesWithinPass(join, sources[left]) != ReachesWithinPass(join, sources[right]) ||
                AreExclusive(sources[left], sources[right]))
                continue;
            return true;
        }

        return false;
    }

    private bool IsReachable(string source, string target, bool requireEdge = false)
    {
        var key = (source, target, requireEdge);
        if (!_reachable.TryGetValue(key, out var reachable))
            _reachable[key] = reachable = Search(source, target, requireEdge);
        return reachable;
    }

    private bool Search(string source, string target, bool requireEdge)
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

    // Whether target is reachable from source without entering a node that dominates source (other than source itself).
    private bool ReachesWithinPass(string source, string target)
    {
        _dominators ??= ComputeDominators();
        var blocked = _dominators.TryGetValue(source, out var dominators) ? dominators : [];
        var visited = new HashSet<string>(StringComparer.Ordinal) { source };
        var pending = new Stack<string>([source]);
        while (pending.Count > 0)
            foreach (var next in successors.GetValueOrDefault(pending.Pop(), []))
            {
                if (blocked.Contains(next) || !visited.Add(next))
                    continue;
                if (StringComparer.Ordinal.Equals(next, target))
                    return true;
                pending.Push(next);
            }

        return false;
    }

    /// <summary>Whether every path from every entry to the node passes the candidate.</summary>
    private bool Dominates(string candidate, string node)
    {
        _dominators ??= ComputeDominators();
        return _dominators.TryGetValue(node, out var dominators) && dominators.Contains(candidate);
    }

    // Computed once per analysis: a node's dominators over every legal entry; unreachable nodes have none.
    private Dictionary<string, HashSet<string>> ComputeDominators()
    {
        var entries = entryNodeIds.ToHashSet(StringComparer.Ordinal);
        var reachable = nodeIds.Where(id => entries.Any(entry => IsReachable(entry, id))).ToHashSet(StringComparer.Ordinal);

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

        return dominators;
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
        string? PolicyOf(string id) => structure.NodeMetadata.TryGetValue(id, out var metadata) ? metadata?.PolicyKind : null;
        var nonWaitingJoins = nodeIds
            .Where(id => inboundSources[id].Count > 1 && StringComparer.Ordinal.Equals(PolicyOf(id), FlowchartPolicyKinds.Merge))
            .ToHashSet(StringComparer.Ordinal);
        // A first-wins race cancels its losing paths only in the Flowchart's state: their running activities still
        // commit, while the join where the raced branches meet is released without them. That join does not order
        // them. Joins after it see only the winner's continuation and wait as usual.
        var raceJoins = new HashSet<string>(StringComparer.Ordinal);
        foreach (var race in nodeIds.Where(id => StringComparer.Ordinal.Equals(PolicyOf(id), FlowchartPolicyKinds.FirstWins)))
        {
            var reach = successors[race].Distinct(StringComparer.Ordinal).ToDictionary(branch => branch, branch =>
            {
                var seen = new HashSet<string>(StringComparer.Ordinal) { branch };
                var pending = new Stack<string>([branch]);
                while (pending.Count > 0)
                    foreach (var next in successors[pending.Pop()])
                        if (!StringComparer.Ordinal.Equals(next, race) && seen.Add(next)) pending.Push(next);
                return seen;
            }, StringComparer.Ordinal);
            // A node only one branch reaches still belongs to that branch's race path.
            var owner = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var node in nodeIds)
            {
                var owners = reach.Where(branch => branch.Value.Contains(node)).Select(branch => branch.Key).ToArray();
                if (owners.Length == 1) owner[node] = owners[0];
            }
            raceJoins.UnionWith(nodeIds.Where(id =>
                inboundSources[id].Where(owner.ContainsKey).Select(source => owner[source]).Distinct(StringComparer.Ordinal).Count() > 1));
        }

        // Forks: every node with several distinct successors, through one port or several, since any activity may select
        // several outcomes in one completion (D-2a as amended; narrowing is for the join initiative, #4193).
        var forkBranches = nodeIds
            .Where(id => successors[id].Count > 1)
            .ToDictionary(id => id, id => successors[id], StringComparer.Ordinal);

        return new FlowchartChildResultAnalysis(entryNodeIds, nodeIds, successors, predecessors, inboundSources, nonWaitingJoins, forkBranches, raceJoins);
    }

}
