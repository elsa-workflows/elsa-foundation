namespace Kernel.Statecharts;

// Each region chooses one child. Several regions on a state enter together.
public sealed record Region(string Initial, State[] States);
public sealed record State(string Id, Region[]? Regions = null, string? Entry = null, string? Exit = null, bool Final = false);
public sealed record Rule(string Source, string Event, string Target, string? Action = null, string? When = null);
public sealed record Definition(Region Root, Rule[] Rules);
public sealed record Step(string Phase, string State, string? Activity);
public sealed record Reaction(string? EventId, string[] Next, Step[] Steps);

/// <summary>Pure selection and phase planning for sibling transitions, including transitions between compound siblings.</summary>
public sealed class Chart
{
    private readonly Definition _definition;
    private readonly Dictionary<string, (State State, Region Region, string? Parent, int Depth)> _nodes = [];

    public Chart(Definition definition)
    {
        _definition = definition;
        Index(definition.Root, null, 0);
        foreach (var rule in definition.Rules)
        {
            if (!_nodes.TryGetValue(rule.Source, out var source) || !_nodes.TryGetValue(rule.Target, out var target))
                throw new ArgumentException("Unknown state in transition.");
            if (rule.Source == rule.Target || !ReferenceEquals(source.Region, target.Region))
                throw new NotSupportedException("The spike supports transitions between distinct siblings in one region.");
        }
    }

    public Reaction Start()
    {
        var entered = Enter(_definition.Root.Initial).ToArray();
        return new(null, entered, entered.Select(id => new Step("entry", id, _nodes[id].State.Entry)).ToArray());
    }

    public Reaction? Select(IReadOnlyList<string> active, string eventId, string name, string? value)
    {
        // An eligible descendant shadows its ancestor. A rule's position is its authored order.
        // The sibling-only subset prevents intersecting exits among the remaining regional rules.
        var selected = new List<Rule>();
        foreach (var source in active.OrderByDescending(id => _nodes[id].Depth))
        {
            if (selected.Any(rule => Within(rule.Source, source))) continue;
            var match = _definition.Rules.FirstOrDefault(r => r.Source == source && r.Event == name &&
                (r.When is null || r.When == value));
            if (match is not null) selected.Add(match);
        }
        if (selected.Count == 0) return null;
        var exited = active.Where(id => selected.Any(rule => Within(id, rule.Source)))
            .OrderByDescending(id => _nodes[id].Depth).ToArray();
        var entered = selected.SelectMany(rule => Enter(rule.Target)).ToArray();
        var steps = exited.Select(id => new Step("exit", id, _nodes[id].State.Exit))
            .Concat(selected.Select(rule => new Step("transition", rule.Source, rule.Action)))
            .Concat(entered.Select(id => new Step("entry", id, _nodes[id].State.Entry)));
        return new(eventId, active.Except(exited).Concat(entered).ToArray(), steps.ToArray());
    }

    public bool IsFinal(IEnumerable<string> active) =>
        active.Any(id => _nodes[id].Parent is null && _nodes[id].State.Final);

    private IEnumerable<string> Enter(string id)
    {
        yield return id;
        foreach (var region in _nodes[id].State.Regions ?? [])
            foreach (var child in Enter(region.Initial)) yield return child;
    }

    private bool Within(string id, string ancestor)
    {
        for (string? current = id; current is not null; current = _nodes[current].Parent)
            if (current == ancestor) return true;
        return false;
    }

    private void Index(Region region, string? parent, int depth)
    {
        if (!region.States.Any(state => state.Id == region.Initial))
            throw new ArgumentException($"Unknown initial state {region.Initial}.");
        foreach (var state in region.States)
        {
            if (!_nodes.TryAdd(state.Id, (state, region, parent, depth)))
                throw new ArgumentException($"Duplicate state {state.Id}.");
            if (state.Final && (parent is not null || state.Regions is { Length: > 0 }))
                throw new NotSupportedException("The spike supports a top-level final leaf.");
            foreach (var child in state.Regions ?? []) Index(child, state.Id, depth + 1);
        }
    }
}
