using Elsa.Activities.Bpmn.Contracts;
using Elsa.Activities.Bpmn.Models;

namespace Elsa.Activities.Bpmn.Internal.Behaviors;

/// <summary>
/// Boundary event (spec 120). A single behavior serves both boundary kinds; the interrupt/absorption
/// semantics are owned entirely by <c>BpmnExecutionEngine</c>, so this behavior only routes the
/// boundary's outbound flows when it fires:
/// <list type="bullet">
/// <item>A <b>catch</b> boundary (timer/message/signal) arms a suspending listener child; when that child
/// completes, <see cref="OnChildCompleted"/> routes the outbound flows (the engine has already cancelled
/// the host and/or the sibling listeners for an interrupting fire, before dispatch).</item>
/// <item>An <b>error</b> boundary has no listener; the engine mints an <see cref="BpmnTokenStatus.Active"/>
/// token at the boundary when the host's child faults, so <see cref="OnTokenArrived"/> routes it.</item>
/// </list>
/// Boundary elements bind no child on the arriving-token path (only the catch listener is a bound child,
/// scheduled by the engine's arming), so <see cref="OnTokenArrived"/> emits rather than schedules.
/// </summary>
public sealed class BoundaryEventBehavior : IBpmnElementBehavior
{
    public string ElementFamily => BpmnElementFamilies.BoundaryEvent;
    public string DisplayName => "Boundary Event";

    public BpmnBehaviorDecision OnTokenArrived(IBpmnBehaviorContext context) =>
        Route(context);

    public BpmnBehaviorDecision OnChildCompleted(IBpmnBehaviorContext context) =>
        Route(context);

    private static BpmnBehaviorDecision Route(IBpmnBehaviorContext context)
    {
        var flows = BpmnFlowSelector.SelectTaskFlows(context);
        if (flows.Count == 0 && context.OutboundFlows.Count > 0)
            return BpmnBehaviorDecision.Of(BpmnBehaviorCommand.Fault(
                "bpmn.flow.none-taken",
                $"BPMN boundary event '{context.Element.ElementId}' fired but no outbound sequence flow matched and no default flow is declared."));

        return BpmnBehaviorDecision.Of(BpmnBehaviorCommand.EmitTokens(BpmnFlowSelector.FlowIds(flows)));
    }
}
