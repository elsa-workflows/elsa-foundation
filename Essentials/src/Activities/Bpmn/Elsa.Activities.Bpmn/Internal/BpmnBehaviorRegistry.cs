using Elsa.Activities.Bpmn.Contracts;
using Elsa.Activities.Bpmn.Exceptions;

namespace Elsa.Activities.Bpmn.Internal;

public sealed class BpmnBehaviorRegistry : IBpmnBehaviorRegistry
{
    private readonly Dictionary<string, IBpmnElementBehavior> _behaviors = new(StringComparer.Ordinal);

    public BpmnBehaviorRegistry(IEnumerable<IBpmnElementBehavior> behaviors)
    {
        foreach (var behavior in behaviors)
            Register(behavior);
    }

    public IReadOnlyCollection<IBpmnElementBehavior> Behaviors => _behaviors.Values;

    public bool TryGet(string elementFamily, out IBpmnElementBehavior behavior)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementFamily);
        return _behaviors.TryGetValue(elementFamily, out behavior!);
    }

    public IBpmnElementBehavior GetRequired(string elementFamily)
    {
        if (TryGet(elementFamily, out var behavior))
            return behavior;

        throw new BpmnExecutionException($"No BPMN element behavior is registered for element family '{elementFamily}'.");
    }

    public void Register(IBpmnElementBehavior behavior)
    {
        ArgumentNullException.ThrowIfNull(behavior);
        _behaviors[behavior.ElementFamily] = behavior;
    }
}
