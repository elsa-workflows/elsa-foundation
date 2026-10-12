using Elsa.Activities.Bpmn.Contracts;
using Elsa.Activities.Bpmn.Models;

namespace Elsa.Activities.Bpmn.Internal;

public sealed class BpmnBehaviorContext(
    BpmnBehaviorTrigger trigger,
    BpmnElement element,
    BpmnToken token,
    IReadOnlyCollection<BpmnSequenceFlow> outboundFlows,
    IReadOnlyCollection<BpmnSequenceFlow> inboundFlows,
    IReadOnlyCollection<string> outcomeNames,
    BpmnExecutionState state)
    : IBpmnBehaviorContext
{
    public BpmnBehaviorTrigger Trigger { get; } = trigger;
    public BpmnElement Element { get; } = element;
    public BpmnToken Token { get; } = token;
    public IReadOnlyCollection<BpmnSequenceFlow> OutboundFlows { get; } = outboundFlows.ToArray();
    public IReadOnlyCollection<BpmnSequenceFlow> InboundFlows { get; } = inboundFlows.ToArray();
    public IReadOnlyCollection<string> OutcomeNames { get; } = outcomeNames.ToArray();
    public BpmnExecutionState State { get; } = state;
}
