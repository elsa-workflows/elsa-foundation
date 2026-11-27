using Kernel.Native;

namespace Kernel.Statecharts.Native;

internal sealed partial class StateMachine
{
    private static void ValidateInternalState(MachineState state, SelectionChart graph)
    {
        if (state.Version < 6)
        {
            if (state.Execution is not null || state.Settling is not null || state.Control is not null || state.Evaluating is not null || state.BusinessReady)
                throw new InvalidOperationException("Mixed statechart internal checkpoint.");
            return;
        }
        if (state.Execution is not { Limit: > 0 } execution || state.Settling is not { } settling || state.Inbox is null ||
            state.Scopes is null || state.LegacyScopes || settling.Limit != execution.Limit)
            throw new InvalidOperationException("Invalid statechart internal profile.");
        settling.Validate();
        if (state.Pending is not null && (state.Control is not null || state.Evaluating is not null &&
                (state.BusinessReady || state.Pending.EventId is not null || !settling.PhasePending)) ||
            state.Pending is null && settling.PhasePending || state.Evaluating is { } id &&
                (id != state.SelectedInput || settling.Stable || settling.Block is not null && state.BusinessReady) ||
            state.BusinessReady && (state.Evaluating is null || state.Pending is not null || settling.PhasePending || settling.Stable || settling.Block is not null) ||
            state.Pending is null && !settling.Stable && state.Control is null ||
            settling.Stable && (state.Control is not null || state.SelectedInput is not null || state.Pending is not null) ||
            settling.PhasePending && (state.Pending is null || state.Pending.EventId is not null || state.Selection?.Selected.Length is not > 0) ||
            state.Pending is { EventId: null } && state.Selection?.Selected.Length > 0 && !settling.PhasePending)
            throw new InvalidOperationException("Invalid statechart internal gate.");
        if (state.Control is { } control)
        {
            var spec = control.Spec;
            if (string.IsNullOrEmpty(control.Original) || string.IsNullOrEmpty(control.Current) || spec is null || !Enum.IsDefined(spec.Purpose) ||
                (control.Current == control.Original) != (control.ReboundFrom is null) || control.ReboundFrom == control.Current || control.ReboundFrom is { Length: 0 } ||
                spec.Detail?.Length > 2048 || spec.Purpose != ControlPurpose.Continue && string.IsNullOrEmpty(spec.Detail) ||
                spec.Purpose == ControlPurpose.Loop && (settling.Block is null || state.BusinessReady || spec.Rule is not null || spec.Visit is not null) ||
                spec.Purpose != ControlPurpose.Loop && settling.Block is not null ||
                spec.Purpose == ControlPurpose.Continue && (spec.Rule is not null || spec.Visit is not null || spec.Detail is not null) ||
                spec.Purpose == ControlPurpose.Guard && !graph.Rules.Any(r => r.Id == spec.Rule && (r.Kind == TriggerKind.Event) == state.BusinessReady &&
                    state.Visits!.Any(v => v.State == r.Source && v.Occurrence == spec.Visit)))
                throw new InvalidOperationException("Invalid statechart internal obligation.");
        }
    }

    private static void ValidateInternalContext(CompositeContext context, MachineState state, string? completing)
    {
        if (state.Execution is null) return;
        if (context.RecoveryPolicy < 4) throw new NotSupportedException("Statechart internal recovery profile unavailable.");
        if (state.Control is { } control && (!context.Running.ToHashSet(StringComparer.Ordinal)
            .SetEquals(completing == control.Current ? [] : new[] { control.Current }) ||
            context.RunningWithTags.Any(c => c.Tag != StateMachineControl.Tag)))
            throw new InvalidOperationException("Statechart internal control lost its ordinary child.");
        if (state.Evaluating is { } id) _ = context.ReadActiveInput(id, evaluating: true);
    }
}
