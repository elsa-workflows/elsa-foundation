using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>
/// Proves order only for one complete, unconditional chain of ordinary BPMN tasks. Other BPMN
/// routing, event and occurrence semantics need their own proof; an authored child array is not an order.
/// </summary>
internal sealed class BpmnLinearChildResultAnalysis(IReadOnlyList<string> children)
{
    public ActivityChildResultFacts Assess(string producer, string consumer)
    {
        var producerIndex = Index(producer);
        var consumerIndex = Index(consumer);
        return producerIndex < 0 || consumerIndex < 0
            ? new(ActivityChildResultAvailability.Unknown)
            : new(producerIndex < consumerIndex ? ActivityChildResultAvailability.Definite : ActivityChildResultAvailability.Unavailable,
                IsCyclicBackEdge: false, MayRunConcurrently: false, AreMutuallyExclusive: false);
    }

    public IReadOnlyList<string>? CompletedBefore(string consumer)
    {
        var index = Index(consumer);
        return index < 0 ? null : children.Take(index).Reverse().ToArray();
    }

    private int Index(string child)
    {
        for (var index = 0; index < children.Count; index++)
            if (StringComparer.Ordinal.Equals(children[index], child)) return index;
        return -1;
    }

    public static BpmnLinearChildResultAnalysis? TryCreate(BpmnAuthoredStructure structure)
    {
        if (structure.IsTransaction || structure.Activities.Count > 10000 || structure.Elements.Count < 3 ||
            structure.Activities.Count != structure.Elements.Count - 2 ||
            structure.SequenceFlows.Count != structure.Elements.Count - 1)
            return null;

        var elements = new Dictionary<string, BpmnElement>(StringComparer.Ordinal);
        var activities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var activity in structure.Activities)
            if (activity is null || string.IsNullOrWhiteSpace(activity.NodeId) || !activities.Add(activity.NodeId)) return null;

        var bound = new HashSet<string>(StringComparer.Ordinal);
        BpmnElement? start = null;
        BpmnElement? end = null;
        foreach (var element in structure.Elements)
        {
            if (element is null || !elements.TryAdd(element.ElementId, element) || element.EventDefinitions.Count != 0 ||
                element.Properties.Count != 0 || element.DefaultFlowId is not null || element.AttachedToRef is not null ||
                element.ListenerNodeId is not null || element.LoopCharacteristics is not null || element.IsForCompensation ||
                element.CompensationHandlerElementId is not null || element.IsTransaction || element.TriggeredByEvent)
                return null;

            // CancelActivity is meaningful on boundaries only; its global default is true even on ordinary tasks.
            switch (element.ElementType)
            {
                case BpmnElementTypes.StartEvent when start is null && element.ChildNodeId is null:
                    start = element;
                    break;
                case BpmnElementTypes.EndEvent when end is null && element.ChildNodeId is null:
                    end = element;
                    break;
                case BpmnElementTypes.Task when element.ChildNodeId is { } child && activities.Contains(child) && bound.Add(child):
                    break;
                default:
                    // Includes gateways, other task families, subprocesses/calls, listeners and unbound/ambiguous work.
                    return null;
            }
        }
        if (start is null || end is null || !bound.SetEquals(activities)) return null;

        var flowIds = new HashSet<string>(StringComparer.Ordinal);
        var next = new Dictionary<string, string>(StringComparer.Ordinal);
        var incoming = new HashSet<string>(StringComparer.Ordinal);
        foreach (var flow in structure.SequenceFlows)
            if (flow is null || !flowIds.Add(flow.FlowId) || flow.ConditionOutcome is not null || flow.IsDefault ||
                !elements.ContainsKey(flow.SourceRef) || !elements.ContainsKey(flow.TargetRef) ||
                !next.TryAdd(flow.SourceRef, flow.TargetRef) || !incoming.Add(flow.TargetRef))
                return null;
        if (incoming.Contains(start.ElementId) || next.ContainsKey(end.ElementId)) return null;

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<string>();
        var current = start.ElementId;
        while (visited.Add(current))
        {
            var element = elements[current];
            if (element.ChildNodeId is { } child) ordered.Add(child);
            if (current == end.ElementId)
                return visited.Count == elements.Count ? new(ordered) : null;
            if (!next.TryGetValue(current, out var successor)) return null;
            current = successor;
        }
        return null;
    }
}
