using System.Text.Json;
using Kernel.Native;

namespace Kernel.Statecharts.Native;

public sealed record MachineState(string[] Active, Reaction? Pending, int Cursor = 0, string? Child = null);

public sealed class StateMachine : CompositeActivity, ISignalledComposite
{
    [Input] public Definition ChartDefinition { get; set; } = null!;

    protected override void Execute(CompositeContext context)
    {
        var start = new Chart(ChartDefinition).Start();
        Run(context, new(start.Next, start));
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
        Run(context, state with { Pending = reaction });
    }

    protected override void OnChildCompleted(CompositeContext context, ChildCompleted child)
    {
        var state = State(context);
        if (child.Frame != state.Child || state.Pending is null)
            throw new InvalidOperationException("Statechart callback does not match its pending action.");
        Run(context, state with { Child = null });
    }

    private void Run(CompositeContext context, MachineState state)
    {
        var reaction = state.Pending!;
        while (state.Cursor < reaction.Steps.Length)
        {
            var step = reaction.Steps[state.Cursor];
            state = state with { Cursor = state.Cursor + 1 };
            Record(context, reaction.EventId, step.Phase, step.State);
            if (step.Activity is null) continue;
            var activity = context.Children.Single(child => child.Id == step.Activity);
            context.SetState(state with { Child = context.Schedule(activity) });
            return;
        }
        context.SetState(new MachineState(reaction.Next, null));
        if (reaction.EventId is { } id) Record(context, id, "handled", "");
        if (new Chart(ChartDefinition).IsFinal(reaction.Next)) context.Complete();
    }

    private static MachineState State(CompositeContext context) =>
        context.ReadState<MachineState>() ?? throw new InvalidOperationException("Statechart has not entered.");

    private static void Record(CompositeContext context, string? id, string phase, string state) =>
        context.Record("statechart:" + JsonSerializer.Serialize(new[] { context.FrameId, id, phase, state }));
}
