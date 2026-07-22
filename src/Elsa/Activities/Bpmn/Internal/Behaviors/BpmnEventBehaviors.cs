using Elsa.Activities.Bpmn.Contracts;
using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Activities.Bpmn.Models;

namespace Elsa.Activities.Bpmn.Internal.Behaviors;

/// <summary>
/// Start event (none / timer / message / signal): pass the arriving token straight onto every outbound sequence
/// flow. All four start families share this token behavior — an event-defined start (spec 117) differs only in
/// how its instance is *started* (a publish-time trigger binding + dispatch seeds the token), never in how the
/// seeded token routes. Registered once per start family so diagnostics keep the family's display name.
/// </summary>
public sealed class StartEventBehavior(string elementFamily, string displayName) : IBpmnElementBehavior
{
    public string ElementFamily { get; } = elementFamily;
    public string DisplayName { get; } = displayName;

    public static StartEventBehavior None() => new(BpmnElementFamilies.StartEventNone, "Start Event (None)");
    public static StartEventBehavior Timer() => new(BpmnElementFamilies.StartEventTimer, "Start Event (Timer)");
    public static StartEventBehavior Message() => new(BpmnElementFamilies.StartEventMessage, "Start Event (Message)");
    public static StartEventBehavior Signal() => new(BpmnElementFamilies.StartEventSignal, "Start Event (Signal)");

    public BpmnBehaviorDecision OnTokenArrived(IBpmnBehaviorContext context) =>
        BpmnBehaviorDecision.Of(BpmnBehaviorCommand.EmitTokens(BpmnFlowSelector.FlowIds(context.OutboundFlows)));

    public BpmnBehaviorDecision OnChildCompleted(IBpmnBehaviorContext context) =>
        throw new BpmnExecutionException($"BPMN start event '{context.Element.ElementId}' cannot own a child activity.");
}

/// <summary>None end event: absorb the token.</summary>
public sealed class NoneEndEventBehavior : IBpmnElementBehavior
{
    public string ElementFamily => BpmnElementFamilies.EndEventNone;
    public string DisplayName => "End Event (None)";

    public BpmnBehaviorDecision OnTokenArrived(IBpmnBehaviorContext context) =>
        BpmnBehaviorDecision.Of(BpmnBehaviorCommand.ConsumeToken());

    public BpmnBehaviorDecision OnChildCompleted(IBpmnBehaviorContext context) =>
        throw new BpmnExecutionException($"BPMN end event '{context.Element.ElementId}' cannot own a child activity.");
}

/// <summary>
/// Intermediate catch event (timer/message/signal, spec 116): parks the arriving token and schedules
/// the element's bound suspending child (e.g. <c>Delay</c> for timer, a waiting <c>Event</c> for
/// message/signal). The child holds the durable timer/bookmark through the runtime's existing
/// suspension surface; its completion is an ordinary child completion that routes outbound flows by
/// the shared task selection rules. Graph validation guarantees the child binding exists.
/// </summary>
public sealed class CatchEventBehavior : IBpmnElementBehavior
{
    public string ElementFamily => BpmnElementFamilies.IntermediateCatchEvent;
    public string DisplayName => "Intermediate Catch Event";

    public BpmnBehaviorDecision OnTokenArrived(IBpmnBehaviorContext context) =>
        BpmnBehaviorDecision.Of(BpmnBehaviorCommand.ScheduleChild());

    public BpmnBehaviorDecision OnChildCompleted(IBpmnBehaviorContext context)
    {
        var flows = BpmnFlowSelector.SelectTaskFlows(context);
        if (flows.Count == 0 && context.OutboundFlows.Count > 0)
            return BpmnBehaviorDecision.Of(BpmnBehaviorCommand.Fault(
                "bpmn.flow.none-taken",
                $"BPMN intermediate catch event '{context.Element.ElementId}' completed with outcomes [{string.Join(", ", context.OutcomeNames)}] but no outbound sequence flow matched and no default flow is declared."));

        return BpmnBehaviorDecision.Of(BpmnBehaviorCommand.EmitTokens(BpmnFlowSelector.FlowIds(flows)));
    }
}

/// <summary>
/// Compensate intermediate throw event (spec 124): on token arrival, emit the single
/// <see cref="BpmnBehaviorCommandKind.TriggerCompensation"/> command and nothing else. The engine owns target
/// selection, claiming, and the sequential handler replay; when the replay finishes it routes the throw token's
/// outbound flows through normal task-flow selection. The behavior stays semantics-unaware.
/// </summary>
public sealed class CompensationThrowEventBehavior : IBpmnElementBehavior
{
    public string ElementFamily => BpmnElementFamilies.IntermediateThrowEventCompensation;
    public string DisplayName => "Compensate Throw Event";

    public BpmnBehaviorDecision OnTokenArrived(IBpmnBehaviorContext context) =>
        BpmnBehaviorDecision.Of(BpmnBehaviorCommand.TriggerCompensation());

    public BpmnBehaviorDecision OnChildCompleted(IBpmnBehaviorContext context) =>
        throw new BpmnExecutionException($"BPMN compensate throw event '{context.Element.ElementId}' cannot own a child activity.");
}

/// <summary>
/// Compensate end event (spec 124): on token arrival, emit the single
/// <see cref="BpmnBehaviorCommandKind.TriggerCompensation"/> command; when the replay finishes the engine
/// consumes the token (none-end semantics). The behavior stays semantics-unaware.
/// </summary>
public sealed class CompensationEndEventBehavior : IBpmnElementBehavior
{
    public string ElementFamily => BpmnElementFamilies.EndEventCompensation;
    public string DisplayName => "Compensate End Event";

    public BpmnBehaviorDecision OnTokenArrived(IBpmnBehaviorContext context) =>
        BpmnBehaviorDecision.Of(BpmnBehaviorCommand.TriggerCompensation());

    public BpmnBehaviorDecision OnChildCompleted(IBpmnBehaviorContext context) =>
        throw new BpmnExecutionException($"BPMN compensate end event '{context.Element.ElementId}' cannot own a child activity.");
}

/// <summary>
/// Cancel end event (spec 125): on token arrival, emit the single
/// <see cref="BpmnBehaviorCommandKind.CancelTransaction"/> command and nothing else. The engine owns the
/// stop-then-claim-then-replay sequencing and the <c>Cancelled</c> completion; the behavior stays
/// semantics-unaware. Valid only inside a transaction (enforced by graph validation).
/// </summary>
public sealed class CancelEndEventBehavior : IBpmnElementBehavior
{
    public string ElementFamily => BpmnElementFamilies.EndEventCancel;
    public string DisplayName => "Cancel End Event";

    public BpmnBehaviorDecision OnTokenArrived(IBpmnBehaviorContext context) =>
        BpmnBehaviorDecision.Of(BpmnBehaviorCommand.CancelTransaction());

    public BpmnBehaviorDecision OnChildCompleted(IBpmnBehaviorContext context) =>
        throw new BpmnExecutionException($"BPMN cancel end event '{context.Element.ElementId}' cannot own a child activity.");
}

/// <summary>
/// Escalation intermediate throw event (spec 127): on token arrival, emit a <see cref="BpmnBehaviorCommandKind.RaiseEscalation"/>
/// command followed by <see cref="BpmnBehaviorCommandKind.EmitTokens"/> over its selected outbound flows — the
/// throw signals its parent scope and continues immediately (fire-and-continue; escalation is non-blocking). The
/// engine reads the escalation code from the element and owns the seam-C staging (or the root no-op); the
/// behavior stays semantics-unaware.
/// </summary>
public sealed class EscalationThrowEventBehavior : IBpmnElementBehavior
{
    public string ElementFamily => BpmnElementFamilies.IntermediateThrowEventEscalation;
    public string DisplayName => "Escalation Throw Event";

    public BpmnBehaviorDecision OnTokenArrived(IBpmnBehaviorContext context) =>
        BpmnBehaviorDecision.Of(
            BpmnBehaviorCommand.RaiseEscalation(),
            BpmnBehaviorCommand.EmitTokens(BpmnFlowSelector.FlowIds(BpmnFlowSelector.SelectTaskFlows(context))));

    public BpmnBehaviorDecision OnChildCompleted(IBpmnBehaviorContext context) =>
        throw new BpmnExecutionException($"BPMN escalation throw event '{context.Element.ElementId}' cannot own a child activity.");
}

/// <summary>
/// Escalation end event (spec 127): on token arrival, emit a <see cref="BpmnBehaviorCommandKind.RaiseEscalation"/>
/// command followed by <see cref="BpmnBehaviorCommandKind.ConsumeToken"/> (none-end semantics) — the throw signals
/// its parent scope and consumes its token. The engine owns the seam-C staging (or the root no-op); the behavior
/// stays semantics-unaware.
/// </summary>
public sealed class EscalationEndEventBehavior : IBpmnElementBehavior
{
    public string ElementFamily => BpmnElementFamilies.EndEventEscalation;
    public string DisplayName => "Escalation End Event";

    public BpmnBehaviorDecision OnTokenArrived(IBpmnBehaviorContext context) =>
        BpmnBehaviorDecision.Of(BpmnBehaviorCommand.RaiseEscalation(), BpmnBehaviorCommand.ConsumeToken());

    public BpmnBehaviorDecision OnChildCompleted(IBpmnBehaviorContext context) =>
        throw new BpmnExecutionException($"BPMN escalation end event '{context.Element.ElementId}' cannot own a child activity.");
}

/// <summary>Terminate end event: consume every live token and complete the process immediately.</summary>
public sealed class TerminateEndEventBehavior : IBpmnElementBehavior
{
    public string ElementFamily => BpmnElementFamilies.EndEventTerminate;
    public string DisplayName => "End Event (Terminate)";

    public BpmnBehaviorDecision OnTokenArrived(IBpmnBehaviorContext context) =>
        BpmnBehaviorDecision.Of(BpmnBehaviorCommand.TerminateProcess(
            $"BPMN terminate end event '{context.Element.ElementId}' ended the process."));

    public BpmnBehaviorDecision OnChildCompleted(IBpmnBehaviorContext context) =>
        throw new BpmnExecutionException($"BPMN end event '{context.Element.ElementId}' cannot own a child activity.");
}
