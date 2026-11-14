namespace Kernel.Statecharts;

internal enum InternalDecisionKind { Stable, Automatic, Completion, Blocked }
internal sealed record CompletionOpportunity(string State, string Visit, long Order);
internal sealed record InternalBlock(InternalDecisionKind Kind, string[] Transitions, string? CompletionVisit);
internal sealed record InternalDecision(InternalDecisionKind Kind, InternalProgress Progress, GraphPlan? Plan = null,
    CompletionOpportunity? Completion = null);

// Pure protocol only. The ordinary host must commit decisions and prove actual entry/phase success.
// This is not an incident strategy, recovery API, scheduler or a second configuration writer.
internal sealed record InternalProgress(int Limit, int Steps = 0, long Serial = 0, StateVisit[]? Offered = null,
    CompletionOpportunity[]? Ready = null, bool PhasePending = false, bool Stable = false,
    InternalBlock? Block = null, int Version = 1)
{
    internal void Validate()
    {
        if (Version != 1) throw new NotSupportedException("Unsupported internal-progress version.");
        var offered = Offered ?? [];
        var ready = Ready ?? [];
        if (Limit <= 0 || Steps < 0 || Steps > Limit || Serial < 0 ||
            offered.Any(v => v is null || string.IsNullOrEmpty(v.State) || string.IsNullOrEmpty(v.Occurrence)) ||
            offered.Select(v => v.Occurrence).Distinct(StringComparer.Ordinal).Count() != offered.Length ||
            ready.Any(r => r is null || r.Order <= 0 || r.Order > Serial ||
                !offered.Any(v => v.State == r.State && v.Occurrence == r.Visit)) ||
            ready.Select(r => r.Visit).Distinct(StringComparer.Ordinal).Count() != ready.Length ||
            !ready.Select(r => r.Order).SequenceEqual(ready.Select(r => r.Order).Distinct().Order()) ||
            Stable && (PhasePending || ready.Length != 0 || Block is not null))
            throw new InvalidOperationException("Invalid internal-progress checkpoint.");
        if (Block is { } block && (Steps != Limit || PhasePending ||
            block.Kind is not (InternalDecisionKind.Automatic or InternalDecisionKind.Completion) ||
            block.Transitions is null || block.Transitions.Any(string.IsNullOrEmpty) ||
            block.Transitions.Distinct(StringComparer.Ordinal).Count() != block.Transitions.Length ||
            (block.Kind == InternalDecisionKind.Automatic ? block.Transitions.Length == 0 || block.CompletionVisit is not null
                : !ready.Any(r => r.Visit == block.CompletionVisit))))
            throw new InvalidOperationException("Invalid internal-progress block.");
    }

    internal InternalProgress CompletePhases()
    {
        Validate();
        if (!PhasePending || Block is not null) throw new InvalidOperationException("No internal phases await completion.");
        return this with { PhasePending = false };
    }

    // This means starting semantic evaluation of a genuinely new input, never merely admitting one.
    internal InternalProgress BeginExternal()
    {
        Validate();
        if (!Stable || PhasePending || Block is not null) throw new InvalidOperationException("Internal progress has not settled.");
        return this with { Steps = 0, Stable = false };
    }
}

internal sealed partial class SelectionChart
{
    private string[] Completed(IReadOnlyDictionary<string, StateVisit> active)
    {
        bool FinalChild(string owner) => _children[owner].Any(id => active.ContainsKey(id) && _nodes[id].Kind == NodeKind.Final);
        return active.Keys.Where(id => _nodes[id].Kind switch
        {
            NodeKind.Compound => FinalChild(id),
            NodeKind.Parallel => _children[id].All(FinalChild),
            _ => false
        }).OrderByDescending(Depth).ThenBy(id => id, StringComparer.Ordinal).ToArray();
    }

    internal InternalDecision NextInternal(IEnumerable<StateVisit> visits, InternalProgress progress,
        Func<GraphRule, StateVisit, bool>? guard = null)
    {
        progress.Validate();
        var active = Configuration(visits);
        if (progress.Block is not null) return new(InternalDecisionKind.Blocked, progress);
        if (progress.PhasePending) throw new InvalidOperationException("Internal phases have not completed.");
        var current = active.Values.ToDictionary(v => v.Occurrence, StringComparer.Ordinal);
        if ((progress.Offered ?? []).Any(v => current.TryGetValue(v.Occurrence, out var live) && live.State != v.State))
            throw new InvalidOperationException("Completion visit changed state identity.");
        var offered = (progress.Offered ?? []).Where(v => current.ContainsKey(v.Occurrence)).ToList();
        var completed = Completed(active).ToHashSet(StringComparer.Ordinal);
        var ready = (progress.Ready ?? []).Where(r => current.ContainsKey(r.Visit) && completed.Contains(r.State)).ToList();
        var serial = progress.Serial;
        foreach (var id in completed.OrderByDescending(Depth).ThenBy(id => id, StringComparer.Ordinal))
        {
            var visit = active[id];
            if (offered.Any(v => v.Occurrence == visit.Occurrence)) continue;
            offered.Add(visit);
            ready.Add(new(id, visit.Occurrence, checked(++serial)));
        }
        // Only local copies have changed. A guard fault leaves the caller's progress untouched.
        var next = progress with { Serial = serial, Offered = offered.ToArray(), Ready = ready.ToArray(), Stable = false };
        var automatic = Select(active.Values, (r, _) => r.Kind == TriggerKind.Automatic, guard);
        if (automatic.Selected.Length != 0) return Decide(InternalDecisionKind.Automatic, automatic, null);
        if (ready.Count == 0) return new(InternalDecisionKind.Stable, next with { Stable = true });
        var completion = ready[0];
        var plan = Select(active.Values, (r, v) => r.Kind == TriggerKind.Completion &&
            r.Source == completion.State && v.Occurrence == completion.Visit, guard);
        return Decide(InternalDecisionKind.Completion, plan, completion);

        InternalDecision Decide(InternalDecisionKind kind, GraphPlan plan, CompletionOpportunity? completion)
        {
            if (next.Steps == next.Limit)
                return new(InternalDecisionKind.Blocked, next with { Block = new(kind, plan.Selected.ToArray(), completion?.Visit) });
            return new(kind, next with { Steps = next.Steps + 1, PhasePending = plan.Selected.Length != 0,
                Ready = completion is null ? next.Ready : ready.Skip(1).ToArray() }, plan, completion);
        }
    }
}
