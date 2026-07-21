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
