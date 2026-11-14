namespace Kernel.Statecharts;

internal enum NodeKind { Root, Compound, Parallel, Region, Atomic, Final }
internal enum VisitMode { Internal, Preserve, Reenter }
internal sealed record GraphNode(string Id, NodeKind Kind, string? Parent = null, string? Initial = null, string? Entry = null, string? Exit = null);
internal sealed record GraphRule(string Id, int Order, string Source, string? Target, VisitMode Mode, string Trigger, string? Action = null);
internal sealed record StateVisit(string State, string Occurrence);
internal sealed record GraphStep(string Id, string Phase, string State, string? Activity, string? Visit, string? Transition, string[] After);
internal sealed record GraphPlan(string[] Candidates, string[] Selected, string[] Next, GraphStep[] Steps);

// Structural selection only. The host owns input admission, wave closure, physical visits, values and recovery.
internal sealed class SelectionChart
{
    private readonly Dictionary<string, GraphNode> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string[]> _paths = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string[]> _children;
    private readonly GraphRule[] _rules;
    private readonly string _root;
    private static bool IsState(GraphNode node) => node.Kind is not (NodeKind.Root or NodeKind.Region);
    private static bool Container(GraphNode node) => node.Kind is NodeKind.Root or NodeKind.Compound or NodeKind.Parallel or NodeKind.Region;

    internal SelectionChart(IEnumerable<GraphNode> nodes, IEnumerable<GraphRule> rules)
    {
        foreach (var n in nodes)
            if (string.IsNullOrEmpty(n.Id) || !Enum.IsDefined(n.Kind) || !_nodes.TryAdd(n.Id, n with { }))
                throw new ArgumentException("Invalid or duplicate node identity/kind.");
        var roots = _nodes.Values.Where(n => n.Kind == NodeKind.Root).ToArray();
        if (roots is not [{ Parent: null } root]) throw new ArgumentException("Exactly one root is required.");
        _root = root.Id;
        foreach (var node in _nodes.Values)
        {
            var path = new List<string>();
            for (string? id = node.Id; id is not null; id = _nodes[id].Parent)
            {
                if (!_nodes.ContainsKey(id) || path.Contains(id, StringComparer.Ordinal))
                    throw new ArgumentException("Missing parent or containment cycle.");
                path.Add(id);
            }
            if (path[^1] != _root) throw new ArgumentException("Node is not rooted.");
            _paths.Add(node.Id, path.ToArray());
        }
        _children = _nodes.Keys.ToDictionary(id => id, id => _nodes.Values.Where(n => n.Parent == id)
            .Select(n => n.Id).Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        foreach (var node in _nodes.Values)
        {
            if (node.Id != _root)
            {
                var parent = _nodes[node.Parent!];
                if (!Container(parent) || (node.Kind == NodeKind.Region) != (parent.Kind == NodeKind.Parallel))
                    throw new ArgumentException("Invalid state/region ownership.");
            }
            var children = _children[node.Id];
            if (Container(node))
            {
                if (children.Length == 0) throw new ArgumentException("Container needs children.");
                if (node.Kind == NodeKind.Parallel ? node.Initial is not null : !children.Contains(node.Initial, StringComparer.Ordinal))
                    throw new ArgumentException("Explicit initial child required; parallel states enter every region.");
            }
            else if (children.Length != 0) throw new ArgumentException("A leaf cannot own children.");
        }
        _rules = rules.Select(r => r with { }).OrderBy(r => r.Order).ToArray();
        if (_rules.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != _rules.Length || _rules.Select(r => r.Order).Distinct().Count() != _rules.Length)
            throw new ArgumentException("Unique transition identity and explicit order required.");
        foreach (var r in _rules)
        {
            if (string.IsNullOrEmpty(r.Id) || string.IsNullOrEmpty(r.Trigger) || !Enum.IsDefined(r.Mode) ||
                !_nodes.TryGetValue(r.Source, out var source) || !IsState(source) || source.Kind == NodeKind.Final)
                throw new ArgumentException("Invalid transition source, trigger or mode.");
            if (r.Mode == VisitMode.Internal)
            {
                if (r.Target is not null) throw new ArgumentException("Internal reactions have no target.");
                continue;
            }
            if (r.Target is null || !_nodes.TryGetValue(r.Target, out var target) || !IsState(target))
                throw new ArgumentException("Invalid transition destination.");
            if (r.Mode == VisitMode.Preserve && (source.Kind != NodeKind.Compound || r.Source == r.Target || !Within(r.Target, r.Source)))
                throw new ArgumentException("Preservation requires a compound source and proper descendant target.");
            foreach (var parallel in _nodes.Values.Where(n => n.Kind == NodeKind.Parallel))
                if (r.Source != parallel.Id && r.Target != parallel.Id && Within(r.Source, parallel.Id) && Within(r.Target, parallel.Id) &&
                    Branch(r.Source, parallel.Id) != Branch(r.Target, parallel.Id))
                    throw new ArgumentException("Direct cross-region transitions are unsupported.");
        }
    }

    internal bool Within(string node, string ancestor) => _paths[node].Contains(ancestor, StringComparer.Ordinal);
    private string Branch(string node, string ancestor) => _paths[node][Array.IndexOf(_paths[node], ancestor) - 1];
    internal string? Entry(string state) => _nodes[state].Entry;
    internal bool IsTopFinal(IEnumerable<string> active) => active.Any(id => _nodes[id].Parent == _root && _nodes[id].Kind == NodeKind.Final);

    internal string[] Initial() => EnterBelow(_root, null);
    private string[] EnterBelow(string boundary, string? target)
    {
        var entered = new List<string>();
        var todo = new Stack<string>();
        void Children(GraphNode node)
        {
            if (node.Kind == NodeKind.Parallel)
                foreach (var child in _children[node.Id].Reverse()) todo.Push(child);
            else if (Container(node)) todo.Push(target is not null && target != node.Id && Within(target, node.Id) ? Branch(target, node.Id) : node.Initial!);
        }
        Children(_nodes[boundary]);
        while (todo.TryPop(out var id))
        {
            var node = _nodes[id];
            if (IsState(node)) entered.Add(id);
            Children(node);
        }
        return entered.ToArray();
    }

    private Dictionary<string, StateVisit> Configuration(IEnumerable<StateVisit> visits)
    {
        var active = new Dictionary<string, StateVisit>(StringComparer.Ordinal);
        var occurrences = new HashSet<string>(StringComparer.Ordinal);
        foreach (var visit in visits)
            if (!_nodes.TryGetValue(visit.State, out var node) || !IsState(node) || string.IsNullOrEmpty(visit.Occurrence) ||
                !occurrences.Add(visit.Occurrence) || !active.TryAdd(visit.State, visit with { }))
                throw new ArgumentException("Invalid active visit.");
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var todo = new Stack<string>([_root]);
        while (todo.TryPop(out var id))
        {
            var node = _nodes[id];
            if (IsState(node))
            {
                if (!active.ContainsKey(id)) throw new ArgumentException("Missing active ancestor/region state.");
                reached.Add(id);
            }
            if (!Container(node)) continue;
            if (node.Kind == NodeKind.Parallel) foreach (var child in _children[id]) todo.Push(child);
            else
            {
                var present = _children[id].Where(active.ContainsKey).ToArray();
                if (present.Length != 1) throw new ArgumentException("Exactly one active child required.");
                todo.Push(present[0]);
            }
        }
        if (reached.Count != active.Count) throw new ArgumentException("Inactive branch contains visits.");
        return active;
    }

    private string? Boundary(GraphRule rule)
    {
        if (rule.Mode == VisitMode.Internal) return null;
        if (rule.Source == rule.Target || Within(rule.Target!, rule.Source) && rule.Mode == VisitMode.Reenter)
            return _nodes[rule.Source].Parent;
        if (Within(rule.Target!, rule.Source)) return rule.Source;
        // Proposed ancestor profile: explicitly exit and re-enter the target ancestor, including its regions.
        if (Within(rule.Source, rule.Target!)) return _nodes[rule.Target!].Parent;
        return _paths[rule.Source].Skip(1).First(id => Within(rule.Target!, id));
    }

    private sealed record Candidate(GraphRule Rule, string Visit, string[] Exiting, string[] Entering);
    internal int[][] Prerequisites(IReadOnlyList<(string Phase, string State)> work)
    {
        if (work.Any(w => w.Phase is not ("exit" or "transition" or "entry") || !_nodes.TryGetValue(w.State, out var node) || !IsState(node)))
            throw new ArgumentException("Invalid structural work.");
        var exits = Enumerable.Range(0, work.Count).Where(i => work[i].Phase == "exit").ToArray();
        var actions = Enumerable.Range(0, work.Count).Where(i => work[i].Phase == "transition").ToArray();
        var entries = Enumerable.Range(0, work.Count).Where(i => work[i].Phase == "entry").ToArray();
        string? Nearest(string state, int[] indices) => _paths[state].Skip(1).FirstOrDefault(parent => indices.Any(i => work[i].State == parent));
        return work.Select(w => w.Phase switch
        {
            "exit" => exits.Where(i => work[i].State != w.State && Nearest(work[i].State, exits) == w.State).ToArray(),
            "transition" => exits.ToArray(),
            _ => Nearest(w.State, entries) is { } parent ? entries.Where(i => work[i].State == parent).ToArray() : (actions.Length == 0 ? exits : actions).ToArray()
        }).ToArray();
    }

    internal GraphPlan Select(IEnumerable<StateVisit> visits, Func<GraphRule, StateVisit, bool> matches, Func<GraphRule, StateVisit, bool>? guard = null)
    {
        var active = Configuration(visits);
        var candidates = new List<Candidate>();
        foreach (var source in active.Keys.OrderByDescending(id => _paths[id].Length).ThenBy(id => id, StringComparer.Ordinal))
        {
            if (candidates.Any(c => c.Rule.Source != source && Within(c.Rule.Source, source))) continue;
            var rule = _rules.FirstOrDefault(r => r.Source == source && matches(r, active[source]) && (guard?.Invoke(r, active[source]) ?? true));
            if (rule is null) continue;
            var boundary = Boundary(rule);
            var exiting = boundary is null ? [] : active.Keys.Where(id => boundary == rule.Source
                ? id != boundary && Within(id, boundary) : Within(id, Branch(rule.Source, boundary))).ToArray();
            candidates.Add(new(rule, active[source].Occurrence, exiting, boundary is null ? [] : EnterBelow(boundary, rule.Target)));
        }
        var selected = new List<Candidate>();
        foreach (var c in candidates.OrderBy(c => c.Rule.Order))
            if (!selected.Any(s => c.Exiting.Intersect(s.Exiting, StringComparer.Ordinal).Any() || c.Exiting.Contains(s.Rule.Source, StringComparer.Ordinal) || s.Exiting.Contains(c.Rule.Source, StringComparer.Ordinal)))
                selected.Add(c);
        var exits = selected.SelectMany(c => c.Exiting).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var entries = selected.SelectMany(c => c.Entering).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var steps = exits.Select(id => new GraphStep("exit:" + id, "exit", id, _nodes[id].Exit, active[id].Occurrence, null, []))
            .Concat(selected.Select(c => new GraphStep("action:" + c.Rule.Id, "transition", c.Rule.Source, c.Rule.Action, c.Visit, c.Rule.Id, [])))
            .Concat(entries.Select(id => new GraphStep("entry:" + id, "entry", id, _nodes[id].Entry, null, null, []))).ToArray();
        var requires = Prerequisites(steps.Select(s => (s.Phase, s.State)).ToArray());
        var planned = steps.Select((s, i) => s with { After = requires[i].Select(j => steps[j].Id).ToArray() }).ToArray();
        return new(candidates.OrderBy(c => c.Rule.Order).Select(c => c.Rule.Id).ToArray(), selected.Select(c => c.Rule.Id).ToArray(),
            active.Keys.Except(exits, StringComparer.Ordinal).Concat(entries).Order(StringComparer.Ordinal).ToArray(), planned);
    }
}
