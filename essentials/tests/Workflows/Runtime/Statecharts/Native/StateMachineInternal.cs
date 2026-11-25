using Kernel.Native;

namespace Kernel.Statecharts.Native;

internal sealed partial class StateMachine
{
    private static bool Ready(MachineState state) => state.Pending is null && (state.Execution is null ||
        state.Control is null && state.Evaluating is null && state.Settling is { Stable: true, Block: null, PhasePending: false });

    private void StageEvaluation(CompositeContext context, MachineState state, string id, string name, string? value)
    {
        if (!Ready(state) || state.SelectedInput != id) throw new InvalidOperationException("Statechart cannot stage this evaluation.");
        var input = context.ReadActiveInput(id, evaluating: false);
        if (input.Name != name || input.Value != value) throw new InvalidOperationException("Statechart input differs from its native selection.");
        context.BeginInputEvaluation(id);
        state = state with { Evaluating = id, Settling = state.Settling!.BeginExternal() };
        Record(context, id, "evaluation-started", name);
        ScheduleControl(context, state, new(ControlPurpose.Continue));
    }

    private void ScheduleControl(CompositeContext context, MachineState state, InternalControlSpec spec)
    {
        if (state.Control is not null || state.Pending is not null || context.Running.Count != 0)
            throw new InvalidOperationException("Statechart internal control overlaps required work.");
        var child = context.Children.Single(n => n.Id == state.Execution!.ControlActivity);
        var frame = context.Schedule(child, StateMachineControl.Tag, new Dictionary<string, System.Text.Json.Nodes.JsonNode?>
        { [StateMachineControl.Key] = Values.From(spec) });
        context.SetState(state with { Control = new(frame, frame, spec) });
        context.RequestCheckpoint();
    }

    private void CompleteControl(CompositeContext context, MachineState state, ChildCompleted child)
    {
        var control = state.Control!;
        var result = Values.To<ControlResult>(child.Result);
        if (child.Frame != control.Current || child.Node != state.Execution!.ControlActivity || child.Tag != StateMachineControl.Tag ||
            child.Outcome != "Done" || result?.Frame != child.Frame || result.Source != control.ReboundFrom ||
            control.Spec.Purpose != ControlPurpose.Continue && (control.ReboundFrom is null || control.Current == control.Original))
            throw new InvalidOperationException("Statechart internal completion lacks its exact recovery lineage.");
        state = state with { Control = null };
        if (control.Spec.Purpose == ControlPurpose.Continue)
        {
            if (state.BusinessReady) EvaluateBusiness(context, state);
            else AdvanceInternal(context, state);
            return;
        }
        if (control.Spec.Purpose == ControlPurpose.Loop)
            state = state with { Settling = state.Settling! with { Steps = 0, Block = null } };
        Record(context, state.Evaluating, "internal-recovered", control.Spec.Purpose.ToString());
        ScheduleControl(context, state, new(ControlPurpose.Continue));
    }

    private void EvaluateBusiness(CompositeContext context, MachineState state)
    {
        var input = context.ReadActiveInput(state.Evaluating!, evaluating: true);
        var graph = Compile(context, state.Execution);
        GraphPlan plan;
        try
        {
            plan = graph.Select(state.Visits!, (r, _) => r.Kind == TriggerKind.Event && r.Trigger == input.Name,
                (r, v) => (r.When is null || r.When == input.Value) && Condition(context, state, r, v));
        }
        catch (GuardFailure error) { GuardBlocked(context, state, error); return; }
        if (plan.Selected.Length == 0)
        {
            context.FinishInputEvaluation(input.Id);
            Record(context, input.Id, "unhandled", input.Name);
            ScheduleControl(context, state with { SelectedInput = null, Evaluating = null, BusinessReady = false }, new(ControlPurpose.Continue));
            return;
        }
        context.SelectInputEvaluation(input.Id);
        Run(context, Begin(state with { Evaluating = null, BusinessReady = false }, plan, input.Id, context.FrameId));
    }

    private void AdvanceInternal(CompositeContext context, MachineState state)
    {
        var graph = Compile(context, state.Execution);
        InternalDecision decision;
        try { decision = graph.NextInternal(state.Visits!, state.Settling!, (r, v) => Condition(context, state, r, v)); }
        catch (GuardFailure error) { GuardBlocked(context, state, error); return; }
        state = state with { Settling = decision.Progress };
        Record(context, null, "internal-evaluated", decision.Kind.ToString());
        if (decision.Kind == InternalDecisionKind.Stable)
        {
            if (state.Evaluating is { } id)
            {
                state = state with { BusinessReady = true, Settling = state.Settling! with { Stable = false } };
                Record(context, id, "evaluated", context.ReadActiveInput(id, evaluating: true).Name);
                EvaluateBusiness(context, state);
            }
            else context.SetState(state);
            return;
        }
        if (decision.Kind == InternalDecisionKind.Blocked)
        {
            ScheduleControl(context, state, new(ControlPurpose.Loop, Detail: "Internal progress limit reached: " + decision.Progress.Limit));
            return;
        }
        if (decision.Plan!.Selected.Length == 0) ScheduleControl(context, state, new(ControlPurpose.Continue));
        else Run(context, Begin(state, decision.Plan, null, context.FrameId));
    }

    private sealed class GuardFailure(GraphRule rule, StateVisit visit, Exception error) : Exception(error.Message, error)
    {
        internal string Rule { get; } = rule.Id;
        internal string Visit { get; } = visit.Occurrence;
    }

    private static bool Condition(CompositeContext context, MachineState state, GraphRule rule, StateVisit visit)
    {
        if (state.Execution!.Conditions?.TryGetValue(rule.Id, out var expression) != true) return true;
        var scope = state.Scopes!.Visits[visit.Occurrence];
        if (context.LexicalScopeInfo(scope).Owner != context.FrameId) throw new InvalidOperationException("Statechart guard has a foreign scope.");
        try { return Values.To<bool>(context.EvaluateInScope(Binding.Expr(expression!), scope)); }
        catch (Exception error) when (error is not (OperationCanceledException or OutOfMemoryException))
        { throw new GuardFailure(rule, visit, error); }
    }

    private void GuardBlocked(CompositeContext context, MachineState state, GuardFailure error)
    {
        var detail = error.InnerException!.GetType().FullName + ": " + error.Message;
        ScheduleControl(context, state, new(ControlPurpose.Guard, error.Rule, error.Visit, detail[..Math.Min(detail.Length, 2048)]));
    }
}
