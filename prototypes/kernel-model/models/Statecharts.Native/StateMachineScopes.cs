using Kernel.Native;

namespace Kernel.Statecharts.Native;

// Values remain in the ordinary native owner. This checkpoint contains handles and visit provenance only.
internal sealed record VisitScopes(string Machine, Dictionary<string, string> Visits, bool Retired)
{
    public bool Retired { get; set; } = Retired;
}

internal sealed partial class StateMachine
{
    private static IEnumerable<StateVisit> RequiredScopes(MachineState state)
    {
        if (state.Pending is null || state.Scopes?.Retired != true) return state.Visits!;
        var exited = state.Pending.Steps.Where(s => s.Phase == "exit").Select(s => s.State).ToHashSet(StringComparer.Ordinal);
        var entered = state.Pending.Steps.Select((s, i) => (s, i)).Where(p => p.s.Phase == "entry" &&
            (state.Progress!.Done[p.i] || state.Progress.Running.ContainsValue(p.i))).Select(p => p.s.State).ToHashSet(StringComparer.Ordinal);
        return state.Visits!.Where(v => !exited.Contains(v.State)).Concat(state.PlannedVisits!.Where(v => entered.Contains(v.State)));
    }

    private static MachineState RestoreScopes(MachineState state, SelectionChart graph, int oldVersion)
    {
        if (oldVersion < 4)
        {
            if (state.Scopes is not null || state.LegacyScopes) throw new InvalidOperationException("Mixed statechart scope checkpoint.");
            state = state with { Version = 4, LegacyScopes = true };
        }
        if (state.Scopes is null)
        {
            if (!state.LegacyScopes) throw new InvalidOperationException("Missing statechart scope checkpoint.");
            if (graph.HasDefaults) throw new InvalidOperationException("Legacy statechart checkpoint cannot acquire new declarations.");
            return state;
        }
        var scopes = state.Scopes;
        if (state.LegacyScopes || string.IsNullOrEmpty(scopes.Machine) || scopes.Visits is null ||
            scopes.Visits.Any(p => string.IsNullOrEmpty(p.Key) || string.IsNullOrEmpty(p.Value)) ||
            scopes.Visits.Values.Append(scopes.Machine).Distinct(StringComparer.Ordinal).Count() != scopes.Visits.Count + 1 ||
            !scopes.Visits.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(RequiredScopes(state).Select(v => v.Occurrence)))
            throw new InvalidOperationException("Invalid statechart scope mapping.");
        if (scopes.Retired && (state.Pending is null || state.Pending.Steps.Select((s, i) => (s, i))
            .Any(p => p.s.Phase != "entry" && !state.Progress!.Done[p.i])))
            throw new InvalidOperationException("Statechart source scopes retired before Action barrier.");
        if (!scopes.Retired && state.Pending is { } pending && pending.Steps.Select((s, i) => (s, i))
            .Any(p => p.s.Phase == "entry" && (state.Progress!.Done[p.i] || state.Progress.Running.ContainsValue(p.i))))
            throw new InvalidOperationException("Statechart Entry started before scope retirement.");
        return state;
    }

    private static void ValidateResources(CompositeContext context, MachineState state)
    {
        if (state.Scopes is not { } scopes) return; // Pure legacy probe; materialization is a separate mutation.
        var machine = context.LexicalScopeInfo(scopes.Machine);
        if (machine.Owner != context.FrameId || machine.Parent is not null)
            throw new InvalidOperationException("Invalid statechart machine scope owner.");
        if (!context.OwnedLexicalScopes().ToHashSet(StringComparer.Ordinal).SetEquals(scopes.Visits.Values.Append(scopes.Machine)))
            throw new InvalidOperationException("Statechart lexical resource ownership differs from its visit map.");
    }

    private MachineState MaterializeScopes(CompositeContext context, MachineState state)
    {
        if (state.Scopes is not null) return state;
        var graph = Compile(context);
        if (!state.LegacyScopes || graph.HasDefaults) throw new InvalidOperationException("Unsupported statechart scope migration.");
        var retired = state.Pending?.Steps.Select((s, i) => (s, i)).Any(p => p.s.Phase == "entry" &&
            (state.Progress!.Done[p.i] || state.Progress.Running.ContainsValue(p.i))) == true;
        state = state with { Scopes = new(context.CreateLexicalScope(graph.MachineDefaults), [], retired), LegacyScopes = false };
        var required = RequiredScopes(state).ToArray();
        foreach (var visit in required.OrderBy(v => graph.Depth(v.State))) CreateVisitScope(context, graph, state.Scopes!, visit, required);
        return state;
    }

    private static void CreateVisitScope(CompositeContext context, SelectionChart graph, VisitScopes scopes, StateVisit visit, StateVisit[] configuration)
    {
        if (scopes.Visits.ContainsKey(visit.Occurrence)) throw new InvalidOperationException("Statechart visit initialized twice.");
        var parent = graph.StateParent(visit.State);
        var parentScope = parent is null ? scopes.Machine : scopes.Visits[configuration.Single(v => v.State == parent).Occurrence];
        scopes.Visits.Add(visit.Occurrence, context.CreateLexicalScope(graph.Defaults(visit.State), parentScope));
    }

    private void RetireSources(CompositeContext context, MachineState state)
    {
        var scopes = state.Scopes!;
        if (scopes.Retired) return;
        var graph = Compile(context);
        foreach (var step in state.Pending!.Steps.Where(s => s.Phase == "exit").OrderByDescending(s => graph.Depth(s.State)))
        {
            var visit = state.Visits!.Single(v => v.State == step.State);
            context.ReleaseLexicalScope(scopes.Visits[visit.Occurrence]);
            scopes.Visits.Remove(visit.Occurrence);
        }
        scopes.Retired = true;
    }

    private static void ValidateScopeParents(CompositeContext context, MachineState state, SelectionChart graph)
    {
        if (state.Scopes is not { } scopes) return;
        var required = RequiredScopes(state).ToDictionary(v => v.State, StringComparer.Ordinal);
        foreach (var visit in required.Values)
        {
            var info = context.LexicalScopeInfo(scopes.Visits[visit.Occurrence]);
            var parent = graph.StateParent(visit.State);
            var expected = parent is null ? scopes.Machine : scopes.Visits[required[parent].Occurrence];
            if (info.Owner != context.FrameId || info.Parent != expected)
                throw new InvalidOperationException("Invalid statechart visit scope owner or parent.");
        }
    }
}
