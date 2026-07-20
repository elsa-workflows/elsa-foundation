using Elsa.Activities.Bpmn.Models;

namespace Elsa.Activities.Bpmn.Contracts;

/// <summary>Read-only element/graph/state view handed to <see cref="IBpmnElementBehavior"/> implementations.</summary>
public interface IBpmnBehaviorContext
{
    BpmnBehaviorTrigger Trigger { get; }

    /// <summary>The element the current token sits at.</summary>
    BpmnElement Element { get; }

    /// <summary>The current token.</summary>
    BpmnToken Token { get; }

    IReadOnlyCollection<BpmnSequenceFlow> OutboundFlows { get; }
    IReadOnlyCollection<BpmnSequenceFlow> InboundFlows { get; }

    /// <summary>The completing child's outcome names (empty on token arrival).</summary>
    IReadOnlyCollection<string> OutcomeNames { get; }

    BpmnExecutionState State { get; }
}

public enum BpmnBehaviorTrigger
{
    TokenArrived,
    ChildCompleted
}
