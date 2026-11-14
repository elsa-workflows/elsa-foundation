namespace Kernel.Statecharts;

// Existing prototype authoring profile. Full compiled graphs are private until native/schema integration is qualified.
public sealed record Region(string Initial, State[] States);
public sealed record State(string Id, Region[]? Regions = null, string? Entry = null, string? Exit = null, bool Final = false);
public sealed record Rule(string Source, string Event, string Target, string? Action = null, string? When = null);
public sealed record Definition(Region Root, Rule[] Rules);
public sealed record Step(string Phase, string State, string? Activity);
public sealed record Reaction(string? EventId, string[] Next, Step[] Steps);

/// <summary>Compatibility facade for the prototype's sibling authoring profile over the one compiled planner.</summary>
public sealed class Chart
{
    private readonly SelectionChart _selection;
    internal SelectionChart Compiled => _selection;
    private readonly Rule[] _rules;

    public Chart(Definition definition)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<State>(definition.Root.States);
        while (pending.TryPop(out var state))
        {
            if (!ids.Add(state.Id)) throw new ArgumentException("Duplicate state identity or containment cycle.");
            foreach (var region in state.Regions ?? []) foreach (var child in region.States) pending.Push(child);
        }
        string PrivateId(string stem)
        {
            while (!ids.Add(stem)) stem += "_";
            return stem;
        }
        var root = PrivateId("$root");
        var nodes = new List<GraphNode> { new(root, NodeKind.Root, Initial: definition.Root.Initial) };
        var todo = new Stack<(State State, string Parent)>(definition.Root.States.Select(s => (s, root)));
        var ordinal = 0;
        while (todo.TryPop(out var item))
        {
            var s = item.State;
            var regions = s.Regions ?? [];
            if (s.Final && (item.Parent != root || regions.Length != 0))
                throw new NotSupportedException("The legacy prototype profile supports a top-level final leaf.");
            var kind = s.Final ? NodeKind.Final : regions.Length switch { 0 => NodeKind.Atomic, 1 => NodeKind.Compound, _ => NodeKind.Parallel };
            nodes.Add(new(s.Id, kind, item.Parent, regions.Length == 1 ? regions[0].Initial : null, s.Entry, s.Exit));
            foreach (var region in regions)
            {
                var parent = s.Id;
                if (kind == NodeKind.Parallel)
                {
                    parent = PrivateId("$region:" + ++ordinal);
                    nodes.Add(new(parent, NodeKind.Region, s.Id, region.Initial));
                }
                foreach (var child in region.States) todo.Push((child, parent));
            }
        }
        var byId = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        _rules = definition.Rules.ToArray();
        foreach (var rule in _rules)
        {
            if (!byId.TryGetValue(rule.Source, out var source) || !byId.TryGetValue(rule.Target, out var target))
                throw new ArgumentException("Unknown state in transition.");
            if (source.Id == target.Id || source.Parent != target.Parent)
                throw new NotSupportedException("The legacy prototype profile supports distinct sibling transitions.");
        }
        _selection = new(nodes, _rules.Select((r, i) => new GraphRule("rule:" + i, i, r.Source, r.Target, VisitMode.Reenter, r.Event, r.Action, r.When)));
    }

    public Reaction Start()
    {
        var initial = _selection.Initial();
        return new(null, initial, initial.Select(id => new Step("entry", id, _selection.Entry(id))).ToArray());
    }

    public Reaction? Select(IReadOnlyList<string> active, string eventId, string name, string? value)
    {
        var plan = _selection.Select(active.Select(id => new StateVisit(id, id)), (r, _) => r.Trigger == name,
            (r, _) => _rules[r.Order].When is null || _rules[r.Order].When == value);
        return plan.Selected.Length == 0 ? null : new(eventId, plan.Next, plan.Steps.Select(s => new Step(s.Phase, s.State, s.Activity)).ToArray());
    }

    public bool IsFinal(IEnumerable<string> active) => _selection.IsTopFinal(active);

    public int[][] Prerequisites(Reaction reaction) => _selection.Prerequisites(reaction.Steps.Select(s => (s.Phase, s.State)).ToArray());
}
