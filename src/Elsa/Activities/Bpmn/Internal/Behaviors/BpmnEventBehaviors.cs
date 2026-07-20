using Elsa.Activities.Bpmn.Contracts;
using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Activities.Bpmn.Models;

namespace Elsa.Activities.Bpmn.Internal.Behaviors;

/// <summary>None start event: pass the token straight onto every outbound sequence flow.</summary>
public sealed class NoneStartEventBehavior : IBpmnElementBehavior
{
    public string ElementFamily => BpmnElementFamilies.StartEventNone;
    public string DisplayName => "Start Event (None)";

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
