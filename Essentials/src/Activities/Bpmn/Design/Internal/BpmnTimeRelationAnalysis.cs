using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>
/// How two children of a BPMN process relate in time (spec 266, D-2, research R-6), read from the element graph.
/// </summary>
/// <remarks>
/// <para>A child reachable from another through sequence flows runs after it: parallel and inclusive gateways wait for
/// their inbound tokens. Exclusive and event-based gateways, and any other element with several incoming flows, pass
/// each token on, so another token can run what follows them while the earlier child is still in flight.</para>
/// <para>Two children that do not reach each other are mutually exclusive when an exclusive split dominates both and
/// every path to one starts on a different choice than every path to the other. Exclusive splits are exclusive
/// gateways, the alternative start events, and a host with interrupting boundary events (its normal flow and each
/// interrupting boundary are choices). Every other split (parallel, inclusive and event-based gateways, implicit
/// splits, non-interrupting boundaries) is unordered, and so are event subprocesses and compensation handlers, which
/// have no sequence flows.</para>
/// <para>A non-sequential multi-instance child overlaps itself.</para>
/// </remarks>
internal sealed class BpmnTimeRelationAnalysis
{
    private const string Root = "\u0000root";
    private const string HostEntryPrefix = "\u0000host:";

    private readonly Dictionary<string, BpmnElement> _elements;
    private readonly Dictionary<string, string> _elementOfChild;
    private readonly Dictionary<string, List<Edge>> _outgoing = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _inbound = new(StringComparer.Ordinal);
    private readonly HashSet<string> _nonWaitingJoins = new(StringComparer.Ordinal);
    private readonly Dictionary<(string, string), bool> _reachable = [];
    private Dictionary<string, HashSet<string>>? _dominators;
    private Dictionary<string, HashSet<string>>? _forkCycleRuns;
    private HashSet<string>? _overlappingNodes;
    private bool _computingOverlap;
    private readonly Dictionary<(string, bool), bool> _arrivalsCanOverlap = [];
    private readonly Dictionary<string, HashSet<string>> _reachedWithoutJoin = new(StringComparer.Ordinal);

    private BpmnTimeRelationAnalysis(Dictionary<string, BpmnElement> elements, Dictionary<string, string> elementOfChild)
    {
        _elements = elements;
        _elementOfChild = elementOfChild;
    }

    public ActivityChildTimeRelation Relate(string firstChild, string secondChild)
    {
        lock (_elements)
            return RelateCore(firstChild, secondChild);
    }

    private ActivityChildTimeRelation RelateCore(string firstChild, string secondChild)
    {
        if (!_elementOfChild.TryGetValue(firstChild, out var first) || !_elementOfChild.TryGetValue(secondChild, out var second))
            return ActivityChildTimeRelation.Unspecified;

        if (StringComparer.Ordinal.Equals(firstChild, secondChild))
            return OverlapsItself(first) ? ActivityChildTimeRelation.Unordered : ActivityChildTimeRelation.FirstBeforeSecond;

        // A body and the listener of one event subprocess, or anything paired with a flow-less element.
        if (StringComparer.Ordinal.Equals(first, second) || !InGraph(first) || !InGraph(second))
            return ActivityChildTimeRelation.Unordered;

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
                   !OverlapsALaterPassFor(earlierNode, laterNode) && !OverlapsALaterPassFor(laterNode, earlierNode)
                ? ActivityChildTimeRelation.FirstBeforeSecond
                : ActivityChildTimeRelation.Unordered;
        }
        var firstEarlier = forward && (!backward || Dominates(first, second));
        var (earlier, later) = firstEarlier ? (first, second) : (second, first);
        if (!OrderedThroughJoins(earlier, later) || OverlapsALaterPassFor(earlier, later))
            return ActivityChildTimeRelation.Unordered;
        return firstEarlier ? ActivityChildTimeRelation.FirstBeforeSecond : ActivityChildTimeRelation.SecondBeforeFirst;
    }

    // A branch that an unordered split on a cycle sends off the cycle can still run while the next pass runs, so every
    // node reachable from it overlaps itself and the next pass of the cycle.
    private Dictionary<string, HashSet<string>> ForkCycleRuns()
    {
        if (_forkCycleRuns is not null)
            return _forkCycleRuns;
        var runs = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (fork, edges) in _outgoing)
        {
            var groups = BranchGroups(edges);
            if (groups.Count < 2 || !IsReachable(fork, fork))
                continue;
            // A group (an unconditional flow, or the choices together) none of whose targets returns to the fork.
            foreach (var target in groups.Where(group => !group.Any(target => IsReachable(target, fork))).SelectMany(group => group))
            foreach (var node in _inbound.Keys.Where(node => StringComparer.Ordinal.Equals(node, target) || IsReachable(target, node)))
            {
                if (!runs.TryGetValue(node, out var forks))
                    runs[node] = forks = new HashSet<string>(StringComparer.Ordinal);
                forks.Add(fork);
            }
        }

        return _forkCycleRuns = runs;
    }

    private bool InGraph(string node) => _inbound.ContainsKey(node);

    private bool OverlapsItself(string node)
    {
        if (_elements.TryGetValue(node, out var element) && element.LoopCharacteristics is { IsSequential: false } ||
            ForkCycleRuns().ContainsKey(node))
            return true;
        return InGraph(node) && _nonWaitingJoins.Any(join =>
            (StringComparer.Ordinal.Equals(join, node) || IsReachable(join, node)) && ArrivalsCanOverlap(join));
    }

    // Graph nodes that can run twice at once. An exclusive split among them can send its runs down different choices,
    // so it does not make those choices exclusive. Computed with the plain exclusivity rule to avoid recursion.
    private HashSet<string> OverlappingNodes()
    {
        if (_overlappingNodes is not null)
            return _overlappingNodes;
        _computingOverlap = true;
        try
        {
            return _overlappingNodes = _inbound.Keys.Where(node => !StringComparer.Ordinal.Equals(node, Root) && OverlapsItself(node))
                .ToHashSet(StringComparer.Ordinal);
        }
        finally
        {
            _computingOverlap = false;
        }
    }

    // Cached per join, separately while the overlapping nodes are computed (exclusivity then ignores overlaps).
    private bool ArrivalsCanOverlap(string join)
    {
        var key = (join, _computingOverlap);
        if (!_arrivalsCanOverlap.TryGetValue(key, out var overlap))
            _arrivalsCanOverlap[key] = overlap = ComputeArrivalsCanOverlap(join);
        return overlap;
    }

    private bool ComputeArrivalsCanOverlap(string join)
    {
        // Every incoming flow is its own token, judged with the choice it leaves its source on.
        var arrivals = Arrivals(join);
        for (var left = 0; left < arrivals.Count; left++)
        for (var right = left + 1; right < arrivals.Count; right++)
        {
            // Two arrivals each run the join. They are in flight together unless they exclude each other, or exactly one of
            // them is reached from the join within one pass: that one is the way back of a loop and only comes after the
            // join's run for the other.
            if (ReachesWithinPass(join, arrivals[left].Source) != ReachesWithinPass(join, arrivals[right].Source) ||
                ArrivalsExclude(arrivals[left], arrivals[right]))
                continue;
            return true;
        }

        return AForkSendsTwoRunsBack(join, arrivals.Select(arrival => arrival.Source).ToHashSet(StringComparer.Ordinal));
    }

    // A fork that sends two branches into the join along different arrivals puts two runs of the join in flight
    // together, whatever pass each arrival closes and wherever the fork sits (before the loop or on it). Branches that
    // meet in a waiting join first reach the same arrivals and send one run.
    private bool AForkSendsTwoRunsBack(string join, HashSet<string> sources) =>
        _outgoing.Any(entry => BranchGroups(entry.Value) is { Count: >= 2 } groups &&
            ReachDifferentArrivals(groups
                .Select(group => group.SelectMany(branch => ArrivalsReachedBy(entry.Key, branch, join, sources)).ToHashSet(StringComparer.Ordinal))
                .ToList()));

    // The branches that run side by side when a node completes: each unconditional flow, plus its choices as one branch
    // (a host's normal flow and interrupting boundaries exclude each other but run beside each non-interrupting one).
    private static List<string[]> BranchGroups(List<Edge> edges)
    {
        var groups = edges.Where(edge => edge.Choice is null).Select(edge => edge.Target).Distinct(StringComparer.Ordinal)
            .Select(target => new[] { target }).ToList();
        var choices = edges.Where(edge => edge.Choice is not null).Select(edge => edge.Target).Distinct(StringComparer.Ordinal).ToArray();
        if (choices.Length > 0)
            groups.Add(choices);
        return groups;
    }

    private IEnumerable<string> SuccessorsOf(string node) =>
        _outgoing.TryGetValue(node, out var edges) ? edges.Select(edge => edge.Target) : [];

    private IEnumerable<string> InboundOf(string node) => _inbound.TryGetValue(node, out var sources) ? sources : [];

    private bool IsWaitingJoin(string node) => InboundOf(node).Count() > 1 && !_nonWaitingJoins.Contains(node);

    // The join's arrivals that a branch reaches before running the join again.
    private HashSet<string> ArrivalsReachedBy(string fork, string branch, string join, ICollection<string> sources)
    {
        var reached = new HashSet<string>(StringComparer.Ordinal);
        if (StringComparer.Ordinal.Equals(branch, join))
        {
            reached.Add(fork);
            return reached;
        }

        // A fork that runs before the join (not after one of its runs) cannot pass a waiting join whose arrival every path
        // from the start reaches only through the join: that arrival waits for the join's run to end. A fork after a run
        // of the join may find such an arrival already there, sent by that earlier run, so nothing is held.
        var holdsAtWaitingJoins = !IsReachable(join, fork);
        var withoutJoin = holdsAtWaitingJoins ? NodesReachedWithoutJoin(join) : [];
        var visited = new HashSet<string>(StringComparer.Ordinal) { branch };
        var queue = new Queue<string>([branch]);
        while (queue.TryDequeue(out var node))
        {
            if (sources.Contains(node))
                reached.Add(node);
            if (holdsAtWaitingJoins && IsWaitingJoin(node) && !InboundOf(node).All(source => StringComparer.Ordinal.Equals(source, fork) || withoutJoin.Contains(source)))
                continue;
            foreach (var next in SuccessorsOf(node))
                if (!StringComparer.Ordinal.Equals(next, join) && visited.Add(next))
                    queue.Enqueue(next);
        }

        return reached;
    }

    // The nodes a run can reach from its start without passing the join.
    private HashSet<string> NodesReachedWithoutJoin(string join)
    {
        if (_reachedWithoutJoin.TryGetValue(join, out var cached))
            return cached;
        var visited = new HashSet<string>(new[] { Root }.Where(node => !StringComparer.Ordinal.Equals(node, join)), StringComparer.Ordinal);
        var queue = new Queue<string>(visited);
        while (queue.TryDequeue(out var node))
            foreach (var next in SuccessorsOf(node))
                if (!StringComparer.Ordinal.Equals(next, join) && visited.Add(next))
                    queue.Enqueue(next);
        return _reachedWithoutJoin[join] = visited;
    }

    private static bool ReachDifferentArrivals(IReadOnlyList<HashSet<string>> arrivals)
    {
        for (var left = 0; left < arrivals.Count; left++)
        for (var right = left + 1; right < arrivals.Count; right++)
            if (arrivals[left].Except(arrivals[right]).Any() && arrivals[right].Except(arrivals[left]).Any())
                return true;
        return false;
    }

    private List<(string Source, string? Choice)> Arrivals(string join) =>
        _outgoing.SelectMany(entry => entry.Value
                .Where(edge => StringComparer.Ordinal.Equals(edge.Target, join))
                .Select(edge => (Source: entry.Key, edge.Choice)))
            .ToList();

    private bool ArrivalsExclude((string Source, string? Choice) left, (string Source, string? Choice) right)
    {
        if (StringComparer.Ordinal.Equals(left.Source, right.Source))
            return left.Choice is not null && right.Choice is not null && !StringComparer.Ordinal.Equals(left.Choice, right.Choice);
        return AreExclusive(left.Source, right.Source) || ArrivalExcludes(left, right.Source) || ArrivalExcludes(right, left.Source);
    }

    // A flow leaving an exclusive split on one choice excludes a node the split reaches only on other choices (the "skip"
    // flow of an optional step excludes the step).
    private bool ArrivalExcludes((string Source, string? Choice) arrival, string node) =>
        arrival.Choice is not null &&
        Dominates(arrival.Source, node) &&
        Choices(arrival.Source, _outgoing[arrival.Source], node) is { Count: > 0 } choices &&
        !choices.Contains(arrival.Choice);

    private bool OrderedThroughJoins(string earlier, string later) =>
        _nonWaitingJoins
            .Where(join => !StringComparer.Ordinal.Equals(join, earlier))
            .Where(join => ReachesWithinPass(earlier, join) && (StringComparer.Ordinal.Equals(join, later) || ReachesWithinPass(join, later)))
            .All(join => Dominates(earlier, join) || !OtherArrivalCanOverlap(join, earlier));

    // Another arrival at the join can run it while the earlier child is in flight when it comes from a source that
    // neither follows the earlier child nor excludes it.
    private bool OtherArrivalCanOverlap(string join, string earlier) =>
        Arrivals(join).Any(arrival =>
            !StringComparer.Ordinal.Equals(arrival.Source, earlier) &&
            !ReachesWithinPass(earlier, arrival.Source) &&
            !AreExclusive(arrival.Source, earlier) &&
            !ArrivalExcludes(arrival, earlier));

    private bool AreExclusive(string left, string right)
    {
        foreach (var (split, edges) in _outgoing)
        {
            if (edges.All(edge => edge.Choice is null) || !Dominates(split, left) || !Dominates(split, right))
                continue;
            if (!_computingOverlap && OverlappingNodes().Contains(split))
                continue;
            if (Choices(split, edges, left) is not { Count: > 0 } leftChoices ||
                Choices(split, edges, right) is not { Count: > 0 } rightChoices)
                continue;
            if (!leftChoices.Overlaps(rightChoices))
                return true;
        }

        return false;
    }

    // The choices of a split through which the node is reached without passing the split again; null when some path
    // starts on an edge that is not a choice.
    private HashSet<string>? Choices(string split, IReadOnlyList<Edge> edges, string node)
    {
        var choices = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (!StringComparer.Ordinal.Equals(edge.Target, node) && !IsReachable(edge.Target, node, avoid: split))
                continue;
            if (edge.Choice is null)
                return null;
            choices.Add(edge.Choice);
        }

        return choices;
    }

    private bool IsReachable(string source, string target, string? avoid = null)
    {
        if (avoid is null && _reachable.TryGetValue((source, target), out var cached))
            return cached;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(source);
        var found = false;
        while (pending.Count > 0 && !found)
        {
            var current = pending.Pop();
            foreach (var edge in _outgoing.GetValueOrDefault(current, []))
            {
                if (StringComparer.Ordinal.Equals(edge.Target, target)) { found = true; break; }
                if (StringComparer.Ordinal.Equals(edge.Target, avoid) || !visited.Add(edge.Target)) continue;
                pending.Push(edge.Target);
            }
        }

        if (avoid is null)
            _reachable[(source, target)] = found;
        return found;
    }

    private bool OverlapsALaterPassFor(string earlier, string later) =>
        ForkCycleRuns().TryGetValue(later, out var forks) && forks.Any(fork => IsReachable(earlier, fork) && IsReachable(fork, earlier));

    // Whether target is reachable from source without entering a node that dominates source (other than source itself).
    private bool ReachesWithinPass(string source, string target)
    {
        _dominators ??= ComputeDominators();
        var blocked = _dominators.TryGetValue(source, out var dominators) ? dominators : [];
        var visited = new HashSet<string>(StringComparer.Ordinal) { source };
        var pending = new Stack<string>([source]);
        while (pending.Count > 0)
            foreach (var edge in _outgoing.GetValueOrDefault(pending.Pop(), []))
            {
                if (blocked.Contains(edge.Target) || !visited.Add(edge.Target))
                    continue;
                if (StringComparer.Ordinal.Equals(edge.Target, target))
                    return true;
                pending.Push(edge.Target);
            }

        return false;
    }

    private bool Dominates(string candidate, string node)
    {
        _dominators ??= ComputeDominators();
        return _dominators.TryGetValue(node, out var dominators) && dominators.Contains(candidate);
    }

    private Dictionary<string, HashSet<string>> ComputeDominators()
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal) { Root };
        var pending = new Stack<string>([Root]);
        while (pending.Count > 0)
            foreach (var edge in _outgoing.GetValueOrDefault(pending.Pop(), []))
                if (reachable.Add(edge.Target)) pending.Push(edge.Target);

        var dominators = reachable.ToDictionary(
            id => id,
            id => StringComparer.Ordinal.Equals(id, Root)
                ? new HashSet<string>([Root], StringComparer.Ordinal)
                : new HashSet<string>(reachable, StringComparer.Ordinal),
            StringComparer.Ordinal);
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var node in reachable.Where(id => !StringComparer.Ordinal.Equals(id, Root)))
            {
                var predecessors = _inbound.GetValueOrDefault(node, []).Where(reachable.Contains).Distinct(StringComparer.Ordinal).ToArray();
                var next = predecessors.Length == 0 ? new HashSet<string>(StringComparer.Ordinal) : new HashSet<string>(dominators[predecessors[0]], StringComparer.Ordinal);
                foreach (var predecessor in predecessors.Skip(1))
                    next.IntersectWith(dominators[predecessor]);
                next.Add(node);
                if (dominators[node].SetEquals(next)) continue;
                dominators[node] = next;
                changed = true;
            }
        }

        return dominators;
    }

    private void AddEdge(string source, string target, string? choice)
    {
        if (!_outgoing.TryGetValue(source, out var edges))
            _outgoing[source] = edges = [];
        edges.Add(new(target, choice));
        if (!_inbound.TryGetValue(target, out var sources))
            _inbound[target] = sources = [];
        sources.Add(source);
        _inbound.TryAdd(source, []);
    }

    public static BpmnTimeRelationAnalysis? TryCreate(BpmnAuthoredStructure structure)
    {
        var elements = new Dictionary<string, BpmnElement>(StringComparer.Ordinal);
        var elementOfChild = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var element in structure.Elements)
        {
            if (element is null || string.IsNullOrWhiteSpace(element.ElementId) || !elements.TryAdd(element.ElementId, element))
                return null;
            foreach (var child in new[] { element.ChildNodeId, element.ListenerNodeId })
                if (!string.IsNullOrWhiteSpace(child) && !elementOfChild.TryAdd(child, element.ElementId))
                    return null;
        }

        var analysis = new BpmnTimeRelationAnalysis(elements, elementOfChild);
        var hosts = elements.Values
            .Where(element => element.ElementType == BpmnElementTypes.BoundaryEvent && element.AttachedToRef is not null)
            .GroupBy(element => element.AttachedToRef!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        string EntryOf(string target) => hosts.ContainsKey(target) ? HostEntryPrefix + target : target;

        foreach (var flow in structure.SequenceFlows)
        {
            if (flow is null || !elements.TryGetValue(flow.SourceRef, out var source) || !elements.ContainsKey(flow.TargetRef))
                return null;
            var choice = source.ElementType == BpmnElementTypes.ExclusiveGateway ? flow.FlowId : null;
            analysis.AddEdge(flow.SourceRef, EntryOf(flow.TargetRef), choice);
        }

        // A boundary arms when its host starts: the host and each boundary follow a pseudo entry in front of the host.
        foreach (var (host, boundaries) in hosts)
        {
            var entry = HostEntryPrefix + host;
            analysis.AddEdge(entry, host, boundaries.Any(boundary => boundary.CancelActivity) ? "normal" : null);
            foreach (var boundary in boundaries)
                analysis.AddEdge(entry, boundary.ElementId, boundary.CancelActivity ? boundary.ElementId : null);
        }

        // The virtual root starts the process: each start event is an alternative; other elements without incoming
        // flows start with it. Flow-less event subprocesses and compensation handlers stay out of the graph.
        foreach (var element in elements.Values)
        {
            var id = EntryOf(element.ElementId);
            if (element.TriggeredByEvent || element.IsForCompensation || element.ElementType == BpmnElementTypes.BoundaryEvent)
                continue;
            if (analysis._inbound.TryGetValue(id, out var inbound) && inbound.Count > 0)
                continue;
            analysis.AddEdge(Root, id, element.ElementType == BpmnElementTypes.StartEvent ? element.ElementId : null);
        }

        foreach (var (node, sources) in analysis._inbound)
        {
            if (sources.Count < 2)
                continue;
            var type = elements.TryGetValue(node.StartsWith(HostEntryPrefix, StringComparison.Ordinal) ? node[HostEntryPrefix.Length..] : node, out var element)
                ? element.ElementType
                : null;
            if (type is not BpmnElementTypes.ParallelGateway and not BpmnElementTypes.InclusiveGateway)
                analysis._nonWaitingJoins.Add(node);
        }

        return analysis;
    }

    private sealed record Edge(string Target, string? Choice);
}
