using Elsa.Activities.Bpmn.Contracts;
using Elsa.Activities.Bpmn.Models;

namespace Elsa.Activities.Bpmn.Internal.Behaviors;

/// <summary>
/// Exclusive (XOR) gateway. Join side: each token passes through independently (handled by the engine's
/// join accounting, which never holds tokens at an exclusive gateway). Split side: when a decision
/// child is bound it is scheduled and the first outcome-matching conditional flow is taken on
/// completion; without a child the gateway needs a single outbound flow or a default flow.
/// </summary>
public sealed class ExclusiveGatewayBehavior : IBpmnElementBehavior
{
    public string ElementFamily => BpmnElementFamilies.ExclusiveGateway;
    public string DisplayName => "Exclusive Gateway";

    public BpmnBehaviorDecision OnTokenArrived(IBpmnBehaviorContext context)
    {
        if (context.Element.ChildNodeId is not null)
            return BpmnBehaviorDecision.Of(BpmnBehaviorCommand.ScheduleChild());

        if (context.OutboundFlows.Count == 1)
            return BpmnBehaviorDecision.Of(BpmnBehaviorCommand.EmitTokens(BpmnFlowSelector.FlowIds(context.OutboundFlows)));

        var defaultFlow = BpmnFlowSelector.ResolveDefaultFlow(context);
        if (defaultFlow is not null)
            return BpmnBehaviorDecision.Of(BpmnBehaviorCommand.EmitTokens([defaultFlow.FlowId]));

        return BpmnBehaviorDecision.Of(BpmnBehaviorCommand.Fault(
            "bpmn.gateway.no-flow-selected",
            $"BPMN exclusive gateway '{context.Element.ElementId}' has multiple outbound flows but no bound decision activity and no default flow."));
    }

    public BpmnBehaviorDecision OnChildCompleted(IBpmnBehaviorContext context)
    {
        var flows = BpmnFlowSelector.SelectExclusiveFlow(context);
        if (flows.Count == 0)
            return BpmnBehaviorDecision.Of(BpmnBehaviorCommand.Fault(
                "bpmn.gateway.no-flow-selected",
                $"BPMN exclusive gateway '{context.Element.ElementId}' decision completed with outcomes [{string.Join(", ", context.OutcomeNames)}] but no conditional flow matched and no default flow is declared."));

        return BpmnBehaviorDecision.Of(BpmnBehaviorCommand.EmitTokens(BpmnFlowSelector.FlowIds(flows)));
    }
}

/// <summary>
/// Parallel (AND) gateway. Join side: the engine's join accounting holds tokens until every inbound
/// flow has arrived. Split side: emit one token per outbound flow.
/// </summary>
public sealed class ParallelGatewayBehavior : IBpmnElementBehavior
{
    public string ElementFamily => BpmnElementFamilies.ParallelGateway;
    public string DisplayName => "Parallel Gateway";

    public BpmnBehaviorDecision OnTokenArrived(IBpmnBehaviorContext context) =>
        BpmnBehaviorDecision.Of(BpmnBehaviorCommand.EmitTokens(BpmnFlowSelector.FlowIds(context.OutboundFlows)));

    public BpmnBehaviorDecision OnChildCompleted(IBpmnBehaviorContext context) =>
        throw new Exceptions.BpmnExecutionException($"BPMN parallel gateway '{context.Element.ElementId}' cannot own a child activity.");
}

/// <summary>
/// Inclusive (OR) gateway. Join side: the engine's activation-aware join accounting waits while any
/// live token or active child upstream can still reach an un-arrived inbound flow. Split side: when a
/// decision child is bound, every outcome-matching conditional flow plus every unconditional flow is
/// taken; without a child, all unconditional flows.
/// </summary>
public sealed class InclusiveGatewayBehavior : IBpmnElementBehavior
{
    public string ElementFamily => BpmnElementFamilies.InclusiveGateway;
    public string DisplayName => "Inclusive Gateway";

    public BpmnBehaviorDecision OnTokenArrived(IBpmnBehaviorContext context)
    {
        if (context.Element.ChildNodeId is not null)
            return BpmnBehaviorDecision.Of(BpmnBehaviorCommand.ScheduleChild());

        var flows = context.OutboundFlows.Where(flow => !flow.IsDefault && flow.ConditionOutcome is null).ToArray();
        if (flows.Length == 0)
        {
            var defaultFlow = BpmnFlowSelector.ResolveDefaultFlow(context);
            if (defaultFlow is not null)
                return BpmnBehaviorDecision.Of(BpmnBehaviorCommand.EmitTokens([defaultFlow.FlowId]));

            return BpmnBehaviorDecision.Of(BpmnBehaviorCommand.Fault(
                "bpmn.gateway.no-flow-selected",
                $"BPMN inclusive gateway '{context.Element.ElementId}' has no unconditional outbound flow, no bound decision activity, and no default flow."));
        }

        return BpmnBehaviorDecision.Of(BpmnBehaviorCommand.EmitTokens(BpmnFlowSelector.FlowIds(flows)));
    }

    public BpmnBehaviorDecision OnChildCompleted(IBpmnBehaviorContext context)
    {
        var flows = BpmnFlowSelector.SelectInclusiveFlows(context);
        if (flows.Count == 0)
            return BpmnBehaviorDecision.Of(BpmnBehaviorCommand.Fault(
                "bpmn.gateway.no-flow-selected",
                $"BPMN inclusive gateway '{context.Element.ElementId}' decision completed with outcomes [{string.Join(", ", context.OutcomeNames)}] but no flow matched and no default flow is declared."));

        return BpmnBehaviorDecision.Of(BpmnBehaviorCommand.EmitTokens(BpmnFlowSelector.FlowIds(flows)));
    }
}
