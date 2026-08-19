using Elsa.Activities.Bpmn.Models;

namespace Elsa.Activities.Bpmn.Contracts;

/// <summary>
/// The behavior of one BPMN element family (the BPMN analog of <c>IFlowchartPolicy</c>). Behaviors
/// receive a read-only <see cref="IBpmnBehaviorContext"/> and return <see cref="BpmnBehaviorDecision"/>
/// commands; the <c>BpmnExecutionEngine</c> validates and applies those commands, keeping mutation and
/// scheduling authority inside the engine.
/// </summary>
public interface IBpmnElementBehavior
{
    /// <summary>The element family this behavior handles (see <c>BpmnElementFamilies</c>).</summary>
    string ElementFamily { get; }

    string DisplayName { get; }

    /// <summary>Invoked when a token arrives at an element of this family (post join accounting).</summary>
    BpmnBehaviorDecision OnTokenArrived(IBpmnBehaviorContext context);

    /// <summary>Invoked when the element's bound Elsa child activity completed.</summary>
    BpmnBehaviorDecision OnChildCompleted(IBpmnBehaviorContext context);
}
