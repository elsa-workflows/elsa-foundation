namespace Elsa.Activities.Bpmn.Contracts;

public interface IBpmnBehaviorRegistry
{
    IReadOnlyCollection<IBpmnElementBehavior> Behaviors { get; }
    bool TryGet(string elementFamily, out IBpmnElementBehavior behavior);
    IBpmnElementBehavior GetRequired(string elementFamily);
    void Register(IBpmnElementBehavior behavior);
}
