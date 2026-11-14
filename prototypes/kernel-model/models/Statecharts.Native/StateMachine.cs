using System.Text.Json;
using Kernel.Native;

namespace Kernel.Statecharts.Native;

internal sealed record PhaseProgress(int[][] Requires, bool[] Done, Dictionary<string, int> Running);
internal sealed record MachineState(string[] Active, Reaction? Pending, int Cursor = 0, string? Child = null,
    PhaseProgress? Progress = null, int Version = 1,
    StateVisit[]? Visits = null, StateVisit[]? PlannedVisits = null, long VisitSerial = 0,
    GraphPlan? Selection = null, bool LegacyPending = false);

internal sealed class StateMachine : CompositeActivity, ISignalledComposite
{
    public StateMachine() { }
    [Input] public Definition? ChartDefinition { get; set; }
    [Input] public StructuralDefinition? StructuralDefinition { get; set; }

    private SelectionChart Compile(CompositeContext context)
    {
        if ((ChartDefinition is null) == (StructuralDefinition is null))
            throw new ArgumentException("Exactly one statechart definition profile is required.");
        var graph = StructuralDefinition is { } definition ? new SelectionChart(definition.Nodes, definition.Rules) : new Chart(ChartDefinition!).Compiled;
        foreach (var id in graph.Activities)
            if (context.Children.Count(n => n.Id == id) != 1) throw new ArgumentException("Unknown statechart activity reference: " + id);
        return graph;
    }

    protected override void Execute(CompositeContext context)
    {
        var graph = Compile(context);
        var initial = graph.Initial();
        var work = initial.Select(id => new GraphStep("entry:" + id, "entry", id, graph.Entry(id), null, null, [])).ToArray();
        var requires = graph.Prerequisites(work.Select(w => (w.Phase, w.State)).ToArray());
        var plan = new GraphPlan([], [], initial, work.Select((w, i) => w with { After = requires[i].Select(j => work[j].Id).ToArray() }).ToArray());
        Run(context, Begin(new([], null, Version: 3, Visits: []), plan, null, context.FrameId));
    }

    public bool CanAccept(CompositeContext context) => State(context).Pending is null;

    public void Accept(CompositeContext context, string messageId, CompositeSignal signal)
    {
        var state = State(context);
        var plan = Compile(context).Select(state.Visits!, (r, _) => r.Trigger == signal.Name,
            (r, _) => r.When is null || r.When == signal.Value);
        Record(context, messageId, "evaluated", signal.Name);
        if (plan.Selected.Length == 0)
        {
            Record(context, messageId, "unhandled", signal.Name);
            return;
        }
        Run(context, Begin(state, plan, messageId, context.FrameId));
    }

    protected override void OnChildCompleted(CompositeContext context, ChildCompleted child)
    {
        var state = State(context);
        if (state.Pending is null || !state.Progress!.Running.Remove(child.Frame, out var index))
            throw new InvalidOperationException("Statechart callback does not match its pending action.");
        state.Progress.Done[index] = true;
        Run(context, state);
    }

    private static MachineState Begin(MachineState state, GraphPlan plan, string? eventId, string frame)
    {
        var serial = state.VisitSerial;
        var entering = plan.Steps.Where(w => w.Phase == "entry").Select(w => w.State).ToHashSet(StringComparer.Ordinal);
        var current = state.Visits!.ToDictionary(v => v.State, StringComparer.Ordinal);
        var next = plan.Next.Select(id => entering.Contains(id) ? new StateVisit(id, frame + "/state/" + checked(++serial)) : current[id]).ToArray();
        var indices = plan.Steps.Select((s, i) => (s.Id, i)).ToDictionary(p => p.Id, p => p.i, StringComparer.Ordinal);
        return state with { Pending = new(eventId, plan.Next, plan.Steps.Select(s => new Step(s.Phase, s.State, s.Activity)).ToArray()),
            Cursor = 0, Child = null, Version = 3, VisitSerial = serial, PlannedVisits = next, Selection = plan, LegacyPending = false,
            Progress = new(plan.Steps.Select(s => s.After.Select(id => indices[id]).ToArray()).ToArray(), new bool[plan.Steps.Length], []) };
    }

    private void Run(CompositeContext context, MachineState state)
    {
        var reaction = state.Pending!;
        var progress = state.Progress!;
        if (!progress.Running.Keys.ToHashSet().SetEquals(context.Running))
            throw new InvalidOperationException("Statechart phase lost its ordinary child ownership.");
        bool changed;
        do
        {
            changed = false;
            for (var i = 0; i < reaction.Steps.Length; i++)
            {
                if (progress.Done[i] || progress.Running.ContainsValue(i) || progress.Requires[i].Any(p => !progress.Done[p])) continue;
                var step = reaction.Steps[i];
                Record(context, reaction.EventId, step.Phase, step.State);
                if (step.Activity is null) progress.Done[i] = true;
                else progress.Running.Add(context.Schedule(context.Children.Single(child => child.Id == step.Activity)), i);
                changed = true;
            }
        } while (changed);
        if (progress.Done.Any(done => !done))
        {
            if (progress.Running.Count == 0) throw new InvalidOperationException("Statechart phase has no reachable work.");
            context.SetState(state);
            return;
        }
        context.SetState(new MachineState(reaction.Next, null, Version: 3, Visits: state.PlannedVisits, VisitSerial: state.VisitSerial));
        if (reaction.EventId is { } id) Record(context, id, "handled", "");
        if (Compile(context).IsTopFinal(reaction.Next)) context.Complete();
    }

    private MachineState State(CompositeContext context) => Restore(
        context.ReadState<MachineState>() ?? throw new InvalidOperationException("Statechart has not entered."), Compile(context), context.FrameId);

    // Reader compatibility for the prototype's serial cursor. Conversion is pure during CanAccept probing;
    // its completed prefix and exact pending child are committed by the next ordinary model checkpoint.
    internal static MachineState Restore(MachineState state, Chart chart) => Restore(state, chart.Compiled, "literal-reader");
    private static MachineState Restore(MachineState state, SelectionChart chart, string frame)
    {
        var oldVersion = state.Version;
        if (oldVersion < 3 && (state.Visits is not null || state.PlannedVisits is not null || state.VisitSerial != 0 || state.Selection is not null || state.LegacyPending))
            throw new InvalidOperationException("Mixed statechart identity checkpoint.");
        if (state.Version == 1)
        {
            if (state.Progress is not null) throw new InvalidOperationException("Invalid serial statechart checkpoint.");
            if (state.Pending is null)
            {
                if (state.Cursor != 0 || state.Child is not null || state.Progress is not null)
                    throw new InvalidOperationException("Invalid serial statechart checkpoint.");
                state = state with { Version = 2 };
            }
            else
            {
                var count = state.Pending.Steps.Length;
                if (state.Cursor < 0 || state.Cursor > count || state.Child is not null && state.Cursor == 0)
                    throw new InvalidOperationException("Invalid serial statechart checkpoint.");
                var completed = state.Cursor - (state.Child is null ? 0 : 1);
                var progress = new PhaseProgress(chart.Prerequisites(state.Pending.Steps.Select(s => (s.Phase, s.State)).ToArray()), Enumerable.Range(0, count).Select(i => i < completed).ToArray(), []);
                if (state.Child is { } child) progress.Running.Add(child, state.Cursor - 1);
                state = state with { Version = 2, Cursor = 0, Child = null, Progress = progress };
            }
        }
        if (state.Version is not (2 or 3)) throw new NotSupportedException("Unsupported statechart phase checkpoint version.");
        if (state.Cursor != 0 || state.Child is not null) throw new InvalidOperationException("Invalid statechart phase checkpoint.");
        if (state.Pending is null && state.Progress is not null)
            throw new InvalidOperationException("Invalid statechart phase checkpoint.");
        if (state.Pending is { } pending)
        {
            var p = state.Progress;
            if (p is null || p.Done.Length != pending.Steps.Length || p.Requires.Length != p.Done.Length ||
                p.Running.Values.Distinct().Count() != p.Running.Count || p.Running.Values.Any(i => i < 0 || i >= p.Done.Length || p.Done[i]) ||
                p.Requires.Any(indices => indices.Any(i => i < 0 || i >= p.Done.Length)))
                throw new InvalidOperationException("Invalid statechart phase checkpoint.");
        }
        if (oldVersion < 3)
        {
            long serial = 0;
            var visits = state.Active.Order(StringComparer.Ordinal).Select(id => new StateVisit(id, frame + "/state/" + ++serial)).ToArray();
            var entering = state.Pending?.Steps.Where(w => w.Phase == "entry").Select(w => w.State).ToHashSet(StringComparer.Ordinal) ?? [];
            var next = state.Pending?.Next.Select(id => entering.Contains(id) ? new StateVisit(id, frame + "/state/" + ++serial) : visits.Single(v => v.State == id)).ToArray();
            state = state with { Version = 3, Visits = visits, PlannedVisits = next, VisitSerial = serial, LegacyPending = state.Pending is not null };
        }
        if (state.Visits is null || state.VisitSerial < 0 || !state.Active.ToHashSet(StringComparer.Ordinal).SetEquals(state.Visits.Select(v => v.State)) ||
            state.Visits.Select(v => v.Occurrence).Distinct(StringComparer.Ordinal).Count() != state.Visits.Length)
            throw new InvalidOperationException("Invalid statechart visits.");
        if (state.Pending is null)
        {
            if (state.PlannedVisits is not null || state.Selection is not null || state.LegacyPending)
                throw new InvalidOperationException("Unexpected pending statechart visits.");
        }
        else
        {
            if (state.PlannedVisits is null || !state.Pending.Next.ToHashSet(StringComparer.Ordinal).SetEquals(state.PlannedVisits.Select(v => v.State)) ||
                state.PlannedVisits.Select(v => v.Occurrence).Distinct(StringComparer.Ordinal).Count() != state.PlannedVisits.Length ||
                (state.Selection is null) != state.LegacyPending)
                throw new InvalidOperationException("Invalid pending statechart visits.");
            if (state.Selection is { } plan && (!plan.Next.SequenceEqual(state.Pending.Next) ||
                !plan.Steps.Select(w => new Step(w.Phase, w.State, w.Activity)).SequenceEqual(state.Pending.Steps)))
                throw new InvalidOperationException("Selected statechart plan changed.");
        }
        var all = state.Visits.Concat(state.PlannedVisits ?? []).ToArray();
        var prefix = frame + "/state/";
        if (all.Any(v => !v.Occurrence.StartsWith(prefix, StringComparison.Ordinal) ||
            !long.TryParse(v.Occurrence.AsSpan(prefix.Length), out var serial) || serial <= 0 || serial > state.VisitSerial))
            throw new InvalidOperationException("Invalid statechart visit serial.");
        if (state.Visits.Length != state.Active.Length || state.PlannedVisits is { } planned && planned.Length != state.Pending!.Next.Length)
            throw new InvalidOperationException("Duplicate statechart state visit.");
        if (state.Visits.Length != 0) _ = chart.Select(state.Visits, (_, _) => false);
        else if (state.Pending?.EventId is not null || state.Pending is null)
            throw new InvalidOperationException("Missing statechart active visits.");
        if (state.PlannedVisits is { } nextVisits) _ = chart.Select(nextVisits, (_, _) => false);
        if (state.Selection is { } selected)
        {
            var current = state.Visits.ToDictionary(v => v.State, v => v.Occurrence, StringComparer.Ordinal);
            foreach (var step in selected.Steps.Where(w => w.Phase != "entry"))
                if (!current.TryGetValue(step.State, out var visit) || step.Visit != visit)
                    throw new InvalidOperationException("Selected statechart source visit changed.");
            var entering = selected.Steps.Where(w => w.Phase == "entry").Select(w => w.State).ToHashSet(StringComparer.Ordinal);
            foreach (var visit in state.PlannedVisits!)
                if (entering.Contains(visit.State) ? current.Values.Contains(visit.Occurrence, StringComparer.Ordinal) : current.GetValueOrDefault(visit.State) != visit.Occurrence)
                    throw new InvalidOperationException("Selected statechart next visit changed.");
            var byId = selected.Steps.Select((w, i) => (w.Id, i)).ToDictionary(p => p.Id, p => p.i, StringComparer.Ordinal);
            if (!selected.Steps.Select((w, i) => w.After.Select(id => byId[id]).SequenceEqual(state.Progress!.Requires[i])).All(ok => ok))
                throw new InvalidOperationException("Selected statechart phase dependencies changed.");
        }
        return state;
    }

    private static void Record(CompositeContext context, string? id, string phase, string state) =>
        context.Record("statechart:" + JsonSerializer.Serialize(new[] { context.FrameId, id, phase, state }));
}
