using System.Text.Json;
using Kernel.Native;

namespace Kernel.Statecharts.Native;

public sealed record PhaseProgress(int[][] Requires, bool[] Done, Dictionary<string, int> Running);
public sealed record MachineState(string[] Active, Reaction? Pending, int Cursor = 0, string? Child = null,
    PhaseProgress? Progress = null, int Version = 1);

public sealed class StateMachine : CompositeActivity, ISignalledComposite
{
    [Input] public Definition ChartDefinition { get; set; } = null!;

    protected override void Execute(CompositeContext context)
    {
        var start = new Chart(ChartDefinition).Start();
        Run(context, Begin(new(start.Next, null), start));
    }

    public bool CanAccept(CompositeContext context) => State(context).Pending is null;

    public void Accept(CompositeContext context, string messageId, CompositeSignal signal)
    {
        var state = State(context);
        var reaction = new Chart(ChartDefinition).Select(state.Active, messageId, signal.Name, signal.Value);
        Record(context, messageId, "evaluated", signal.Name);
        if (reaction is null)
        {
            Record(context, messageId, "unhandled", signal.Name);
            return;
        }
        Run(context, Begin(state, reaction));
    }

    protected override void OnChildCompleted(CompositeContext context, ChildCompleted child)
    {
        var state = State(context);
        if (state.Pending is null || !state.Progress!.Running.Remove(child.Frame, out var index))
            throw new InvalidOperationException("Statechart callback does not match its pending action.");
        state.Progress.Done[index] = true;
        Run(context, state);
    }

    private MachineState Begin(MachineState state, Reaction reaction) => state with
    { Pending = reaction, Cursor = 0, Child = null, Version = 2,
      Progress = new(new Chart(ChartDefinition).Prerequisites(reaction), new bool[reaction.Steps.Length], []) };

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
        context.SetState(new MachineState(reaction.Next, null, Version: 2));
        if (reaction.EventId is { } id) Record(context, id, "handled", "");
        if (new Chart(ChartDefinition).IsFinal(reaction.Next)) context.Complete();
    }

    private MachineState State(CompositeContext context) => Restore(
        context.ReadState<MachineState>() ?? throw new InvalidOperationException("Statechart has not entered."), new Chart(ChartDefinition));

    // Reader compatibility for the prototype's serial cursor. Conversion is pure during CanAccept probing;
    // its completed prefix and exact pending child are committed by the next ordinary model checkpoint.
    internal static MachineState Restore(MachineState state, Chart chart)
    {
        if (state.Version == 1)
        {
            if (state.Progress is not null) throw new InvalidOperationException("Invalid serial statechart checkpoint.");
            if (state.Pending is null)
            {
                if (state.Cursor != 0 || state.Child is not null || state.Progress is not null)
                    throw new InvalidOperationException("Invalid serial statechart checkpoint.");
                return state with { Version = 2 };
            }
            var count = state.Pending.Steps.Length;
            if (state.Cursor < 0 || state.Cursor > count || state.Child is not null && state.Cursor == 0)
                throw new InvalidOperationException("Invalid serial statechart checkpoint.");
            var completed = state.Cursor - (state.Child is null ? 0 : 1);
            var progress = new PhaseProgress(chart.Prerequisites(state.Pending), Enumerable.Range(0, count).Select(i => i < completed).ToArray(), []);
            if (state.Child is { } child) progress.Running.Add(child, state.Cursor - 1);
            state = state with { Version = 2, Cursor = 0, Child = null, Progress = progress };
        }
        if (state.Version != 2) throw new NotSupportedException("Unsupported statechart phase checkpoint version.");
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
        return state;
    }

    private static void Record(CompositeContext context, string? id, string phase, string state) =>
        context.Record("statechart:" + JsonSerializer.Serialize(new[] { context.FrameId, id, phase, state }));
}
