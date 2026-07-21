using System.Text.Json;
using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Runtime.Core.Models;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>
/// The parsed, validated executable BPMN graph: elements, sequence flows, child bindings, and family
/// resolution. Construction validates the structural invariants of this engine slice (unique ids,
/// resolvable references, ≥1 none start event, event/gateway binding rules, single default flow per
/// element, acyclicity) so the engine can navigate without re-checking.
/// </summary>
public sealed class BpmnGraph
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IReadOnlyDictionary<string, BpmnElement> _elementsById;
    private readonly IReadOnlyDictionary<string, BpmnSequenceFlow> _flowsById;
    private readonly IReadOnlyDictionary<string, ExecutableNode> _childrenByNodeId;
    private readonly IReadOnlyDictionary<string, BpmnElement> _elementsByChildNodeId;
    private readonly ILookup<string, BpmnSequenceFlow> _outboundBySource;
    private readonly ILookup<string, BpmnSequenceFlow> _inboundByTarget;

    private BpmnGraph(
        IReadOnlyCollection<BpmnElement> elements,
        IReadOnlyCollection<BpmnSequenceFlow> sequenceFlows,
        IReadOnlyDictionary<string, ExecutableNode> childrenByNodeId)
    {
        Elements = elements;
        SequenceFlows = sequenceFlows;
        _childrenByNodeId = childrenByNodeId;
        _elementsById = elements.ToDictionary(element => element.ElementId, StringComparer.Ordinal);
        _flowsById = sequenceFlows.ToDictionary(flow => flow.FlowId, StringComparer.Ordinal);
        _elementsByChildNodeId = elements
            .Where(element => element.ChildNodeId is not null)
            .ToDictionary(element => element.ChildNodeId!, StringComparer.Ordinal);
        _outboundBySource = sequenceFlows.ToLookup(flow => flow.SourceRef, StringComparer.Ordinal);
        _inboundByTarget = sequenceFlows.ToLookup(flow => flow.TargetRef, StringComparer.Ordinal);
        StartEvents = elements
            .Where(element => StringComparer.Ordinal.Equals(element.ElementType, BpmnElementTypes.StartEvent))
            .ToArray();
    }

    public IReadOnlyCollection<BpmnElement> Elements { get; }
    public IReadOnlyCollection<BpmnSequenceFlow> SequenceFlows { get; }
    public IReadOnlyCollection<BpmnElement> StartEvents { get; }

    public static BpmnGraph From(ExecutableNode executableNode)
    {
        ArgumentNullException.ThrowIfNull(executableNode);

        var slot = executableNode.ChildSlots.FirstOrDefault(slot => StringComparer.Ordinal.Equals(slot.Name, BpmnProcessActivity.ActivitiesSlotName));
        var children = slot?.Activities.ToArray() ?? [];
        var childrenByNodeId = children.ToDictionary(child => child.ExecutableNodeId, StringComparer.Ordinal);
        var structure = ReadStructure(executableNode);
        var elements = structure?.Elements ?? [];
        var flows = structure?.SequenceFlows ?? [];

        Validate(elements, flows, childrenByNodeId);

        return new BpmnGraph(elements, flows, childrenByNodeId);
    }

    public BpmnElement GetRequiredElement(string elementId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);

        if (_elementsById.TryGetValue(elementId, out var element))
            return element;

        throw new BpmnExecutionException($"BPMN element '{elementId}' does not exist in the process graph.");
    }

    public BpmnSequenceFlow GetRequiredFlow(string flowId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flowId);

        if (_flowsById.TryGetValue(flowId, out var flow))
            return flow;

        throw new BpmnExecutionException($"BPMN sequence flow '{flowId}' does not exist in the process graph.");
    }

    public BpmnElement? FindElementByChildNodeId(string childNodeId) =>
        _elementsByChildNodeId.TryGetValue(childNodeId, out var element) ? element : null;

    public ExecutableNode GetRequiredChildNode(string childNodeId)
    {
        if (_childrenByNodeId.TryGetValue(childNodeId, out var child))
            return child;

        throw new BpmnExecutionException($"BPMN child activity node '{childNodeId}' does not exist in child slot '{BpmnProcessActivity.ActivitiesSlotName}'.");
    }

    public IReadOnlyCollection<BpmnSequenceFlow> OutboundFlows(string elementId) =>
        _outboundBySource[elementId].ToArray();

    public IReadOnlyCollection<BpmnSequenceFlow> InboundFlows(string elementId) =>
        _inboundByTarget[elementId].ToArray();

    public BpmnSequenceFlow? GetDefaultFlow(BpmnElement element)
    {
        if (element.DefaultFlowId is not null)
            return GetRequiredFlow(element.DefaultFlowId);

        return _outboundBySource[element.ElementId].FirstOrDefault(flow => flow.IsDefault);
    }

    /// <summary>True when any path over sequence flows leads from <paramref name="sourceElementId"/> to <paramref name="targetElementId"/>.</summary>
    public bool CanReach(string sourceElementId, string targetElementId)
    {
        if (StringComparer.Ordinal.Equals(sourceElementId, targetElementId))
            return true;

        var visited = new HashSet<string>(StringComparer.Ordinal) { sourceElementId };
        var queue = new Queue<string>();
        queue.Enqueue(sourceElementId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var next in _outboundBySource[current].Select(flow => flow.TargetRef))
            {
                if (StringComparer.Ordinal.Equals(next, targetElementId))
                    return true;

                if (visited.Add(next))
                    queue.Enqueue(next);
            }
        }

        return false;
    }

    private static BpmnStructure? ReadStructure(ExecutableNode executableNode)
    {
        if (executableNode.Structure is null)
            return null;

        if (!StringComparer.Ordinal.Equals(executableNode.Structure.Kind, BpmnProcessActivity.StructureKind))
            throw new BpmnExecutionException($"BPMN executable node '{executableNode.ExecutableNodeId}' has unsupported structure kind '{executableNode.Structure.Kind}'.");

        if (!StringComparer.Ordinal.Equals(executableNode.Structure.SchemaVersion, BpmnProcessActivity.StructureSchemaVersion))
            throw new BpmnExecutionException($"BPMN executable node '{executableNode.ExecutableNodeId}' has unsupported structure schema version '{executableNode.Structure.SchemaVersion}'.");

        try
        {
            return executableNode.Structure.Payload.Deserialize<BpmnStructure>(SerializerOptions)
                   ?? throw new BpmnExecutionException($"BPMN executable node '{executableNode.ExecutableNodeId}' structure resolved to null.");
        }
        catch (BpmnExecutionException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            throw new BpmnExecutionException($"BPMN executable node '{executableNode.ExecutableNodeId}' structure is not a valid BPMN structure payload.", exception);
        }
    }

    private static void Validate(
        IReadOnlyCollection<BpmnElement> elements,
        IReadOnlyCollection<BpmnSequenceFlow> flows,
        IReadOnlyDictionary<string, ExecutableNode> childrenByNodeId)
    {
        if (elements.Select(element => element.ElementId).Distinct(StringComparer.Ordinal).Count() != elements.Count)
            throw new BpmnExecutionException("BPMN structure contains duplicate element ids.");

        if (flows.Select(flow => flow.FlowId).Distinct(StringComparer.Ordinal).Count() != flows.Count)
            throw new BpmnExecutionException("BPMN structure contains duplicate sequence flow ids.");

        var elementIds = elements.Select(element => element.ElementId).ToHashSet(StringComparer.Ordinal);
        foreach (var flow in flows)
        {
            if (!elementIds.Contains(flow.SourceRef))
                throw new BpmnExecutionException($"BPMN sequence flow '{flow.FlowId}' source '{flow.SourceRef}' does not exist.");
            if (!elementIds.Contains(flow.TargetRef))
                throw new BpmnExecutionException($"BPMN sequence flow '{flow.FlowId}' target '{flow.TargetRef}' does not exist.");
        }

        var boundChildNodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in elements)
        {
            // Resolving the family also rejects unsupported element types and event definitions.
            var family = BpmnElementFamilies.Resolve(element);

            if (element.ChildNodeId is not null)
            {
                if (!childrenByNodeId.ContainsKey(element.ChildNodeId))
                    throw new BpmnExecutionException($"BPMN element '{element.ElementId}' binds child activity node '{element.ChildNodeId}', which does not exist in child slot '{BpmnProcessActivity.ActivitiesSlotName}'.");
                if (!boundChildNodeIds.Add(element.ChildNodeId))
                    throw new BpmnExecutionException($"BPMN child activity node '{element.ChildNodeId}' is bound by more than one element.");
            }

            switch (family)
            {
                case BpmnElementFamilies.StartEventNone:
                case BpmnElementFamilies.StartEventTimer:
                case BpmnElementFamilies.StartEventMessage:
                case BpmnElementFamilies.StartEventSignal:
                case BpmnElementFamilies.EndEventNone:
                case BpmnElementFamilies.EndEventTerminate:
                case BpmnElementFamilies.ParallelGateway:
                case BpmnElementFamilies.EventBasedGateway:
                    if (element.ChildNodeId is not null)
                        throw new BpmnExecutionException($"BPMN element '{element.ElementId}' ({element.ElementType}) cannot bind a child activity.");
                    break;
                case BpmnElementFamilies.SubProcess:
                    if (element.ChildNodeId is null)
                        throw new BpmnExecutionException($"BPMN subprocess '{element.ElementId}' requires a bound child activity (for example a nested BPMN process).");
                    break;
                case BpmnElementFamilies.IntermediateCatchEvent:
                    if (element.ChildNodeId is null)
                        throw new BpmnExecutionException($"BPMN intermediate catch event '{element.ElementId}' requires a bound suspending child activity (for example Delay for timer, Event for message/signal).");
                    break;
            }
        }

        foreach (var unboundChildNodeId in childrenByNodeId.Keys.Where(nodeId => !boundChildNodeIds.Contains(nodeId)))
            throw new BpmnExecutionException($"BPMN child activity node '{unboundChildNodeId}' is not bound to any element.");

        var startEvents = elements.Where(element => StringComparer.Ordinal.Equals(element.ElementType, BpmnElementTypes.StartEvent)).ToArray();
        if (elements.Count > 0 && startEvents.Length == 0)
            throw new BpmnExecutionException("BPMN structure requires at least one start event.");

        var inboundByTarget = flows.ToLookup(flow => flow.TargetRef, StringComparer.Ordinal);
        var outboundBySource = flows.ToLookup(flow => flow.SourceRef, StringComparer.Ordinal);

        foreach (var startEvent in startEvents)
        {
            if (inboundByTarget[startEvent.ElementId].Any())
                throw new BpmnExecutionException($"BPMN start event '{startEvent.ElementId}' cannot have inbound sequence flows.");
        }

        foreach (var endEvent in elements.Where(element => StringComparer.Ordinal.Equals(element.ElementType, BpmnElementTypes.EndEvent)))
        {
            if (outboundBySource[endEvent.ElementId].Any())
                throw new BpmnExecutionException($"BPMN end event '{endEvent.ElementId}' cannot have outbound sequence flows.");
        }

        foreach (var element in elements)
        {
            var defaults = outboundBySource[element.ElementId].Where(flow => flow.IsDefault).ToArray();
            if (defaults.Length > 1)
                throw new BpmnExecutionException($"BPMN element '{element.ElementId}' declares more than one default sequence flow.");

            if (element.DefaultFlowId is not null)
            {
                var referenced = flows.FirstOrDefault(flow => StringComparer.Ordinal.Equals(flow.FlowId, element.DefaultFlowId))
                                 ?? throw new BpmnExecutionException($"BPMN element '{element.ElementId}' default flow '{element.DefaultFlowId}' does not exist.");
                if (!StringComparer.Ordinal.Equals(referenced.SourceRef, element.ElementId))
                    throw new BpmnExecutionException($"BPMN element '{element.ElementId}' default flow '{element.DefaultFlowId}' does not originate from it.");
                if (defaults.Length == 1 && !StringComparer.Ordinal.Equals(defaults[0].FlowId, element.DefaultFlowId))
                    throw new BpmnExecutionException($"BPMN element '{element.ElementId}' declares conflicting default flows '{defaults[0].FlowId}' and '{element.DefaultFlowId}'.");
            }
        }

        ValidateEventBasedGateways(elements, outboundBySource, inboundByTarget);

        ValidateAcyclic(elements, outboundBySource);
    }

    /// <summary>
    /// Event-based gateway rules (spec 119 D1): at least two outbound flows; every outbound flow targets an
    /// intermediate catch event whose only inbound flow is this gateway's; the gateway's outbound flows carry
    /// no outcome condition and no default (the race is decided by stimulus arrival, not by a condition, so an
    /// authored condition/default is rejected rather than silently ignored).
    /// </summary>
    private static void ValidateEventBasedGateways(
        IReadOnlyCollection<BpmnElement> elements,
        ILookup<string, BpmnSequenceFlow> outboundBySource,
        ILookup<string, BpmnSequenceFlow> inboundByTarget)
    {
        var elementsById = elements.ToDictionary(element => element.ElementId, StringComparer.Ordinal);

        foreach (var gateway in elements.Where(element => StringComparer.Ordinal.Equals(element.ElementType, BpmnElementTypes.EventBasedGateway)))
        {
            var outbound = outboundBySource[gateway.ElementId].ToArray();
            if (outbound.Length < 2)
                throw new BpmnExecutionException($"BPMN event-based gateway '{gateway.ElementId}' must have at least two outbound sequence flows (a race needs at least two catch events); it has {outbound.Length}.");

            if (gateway.DefaultFlowId is not null)
                throw new BpmnExecutionException($"BPMN event-based gateway '{gateway.ElementId}' cannot declare a default sequence flow.");

            foreach (var flow in outbound)
            {
                if (flow.ConditionOutcome is not null || flow.IsDefault)
                    throw new BpmnExecutionException($"BPMN event-based gateway '{gateway.ElementId}' outbound flow '{flow.FlowId}' cannot carry a condition or be a default flow; the race is decided by stimulus arrival.");

                if (!elementsById.TryGetValue(flow.TargetRef, out var target) ||
                    !StringComparer.Ordinal.Equals(target.ElementType, BpmnElementTypes.IntermediateCatchEvent))
                    throw new BpmnExecutionException($"BPMN event-based gateway '{gateway.ElementId}' outbound flow '{flow.FlowId}' must target an intermediate catch event; it targets '{flow.TargetRef}'.");

                var targetInbound = inboundByTarget[flow.TargetRef].Count();
                if (targetInbound != 1)
                    throw new BpmnExecutionException($"BPMN event-based gateway '{gateway.ElementId}' targets catch event '{flow.TargetRef}', which must have exactly one inbound flow (the gateway); it has {targetInbound}.");
            }
        }
    }

    /// <summary>
    /// This engine slice rejects cyclic graphs: BPMN loops arrive with loop characteristics and
    /// iteration scopes in the events tier (see the module README's phasing notes).
    /// </summary>
    private static void ValidateAcyclic(IReadOnlyCollection<BpmnElement> elements, ILookup<string, BpmnSequenceFlow> outboundBySource)
    {
        var states = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var element in elements)
        {
            if (Visit(element.ElementId) is { } cycleElementId)
                throw new BpmnExecutionException($"BPMN structure contains a cycle through element '{cycleElementId}'; cyclic graphs are not supported by this engine slice.");
        }

        return;

        string? Visit(string elementId)
        {
            if (states.TryGetValue(elementId, out var known))
                return known == 1 ? elementId : null;

            states[elementId] = 1;
            foreach (var flow in outboundBySource[elementId])
            {
                if (Visit(flow.TargetRef) is { } cycleElementId)
                    return cycleElementId;
            }

            states[elementId] = 2;
            return null;
        }
    }
}
