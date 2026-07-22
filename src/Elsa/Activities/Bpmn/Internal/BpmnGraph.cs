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
/// element) so the engine can navigate without re-checking, and precomputes the backward (loop-back) flow
/// set so the engine can mint loop-iteration keys (spec 122). Cyclic graphs are executable; the structural
/// rules still forbid a loop-back into a start event, a boundary event, or an event-gateway-armed catch.
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
    private readonly ILookup<string, BpmnElement> _boundariesByHost;
    private readonly IReadOnlySet<string> _backwardFlowIds;

    private BpmnGraph(
        IReadOnlyCollection<BpmnElement> elements,
        IReadOnlyCollection<BpmnSequenceFlow> sequenceFlows,
        IReadOnlyDictionary<string, ExecutableNode> childrenByNodeId,
        bool isTransaction)
    {
        Elements = elements;
        SequenceFlows = sequenceFlows;
        IsTransaction = isTransaction;
        _childrenByNodeId = childrenByNodeId;
        _elementsById = elements.ToDictionary(element => element.ElementId, StringComparer.Ordinal);
        _flowsById = sequenceFlows.ToDictionary(flow => flow.FlowId, StringComparer.Ordinal);
        _elementsByChildNodeId = elements
            .Where(element => element.ChildNodeId is not null)
            .ToDictionary(element => element.ChildNodeId!, StringComparer.Ordinal);
        _outboundBySource = sequenceFlows.ToLookup(flow => flow.SourceRef, StringComparer.Ordinal);
        _inboundByTarget = sequenceFlows.ToLookup(flow => flow.TargetRef, StringComparer.Ordinal);
        _boundariesByHost = elements
            .Where(element => StringComparer.Ordinal.Equals(element.ElementType, BpmnElementTypes.BoundaryEvent) && element.AttachedToRef is not null)
            .ToLookup(element => element.AttachedToRef!, StringComparer.Ordinal);
        StartEvents = elements
            .Where(element => StringComparer.Ordinal.Equals(element.ElementType, BpmnElementTypes.StartEvent))
            .ToArray();
        _backwardFlowIds = ComputeBackwardFlowIds(elements, _outboundBySource, StartEvents);
    }

    public IReadOnlyCollection<BpmnElement> Elements { get; }
    public IReadOnlyCollection<BpmnSequenceFlow> SequenceFlows { get; }
    public IReadOnlyCollection<BpmnElement> StartEvents { get; }

    /// <summary>Whether this process is a transaction (spec 125): its scope may be cancelled from within by a cancel end event.</summary>
    public bool IsTransaction { get; }

    /// <summary>The loop-closing (backward) sequence flow ids (spec 122); a token that traverses one mints a fresh iteration key.</summary>
    public IReadOnlyCollection<string> BackwardFlowIds => _backwardFlowIds;

    public static BpmnGraph From(ExecutableNode executableNode)
    {
        ArgumentNullException.ThrowIfNull(executableNode);

        var slot = executableNode.ChildSlots.FirstOrDefault(slot => StringComparer.Ordinal.Equals(slot.Name, BpmnProcessActivity.ActivitiesSlotName));
        var children = slot?.Activities.ToArray() ?? [];
        var childrenByNodeId = children.ToDictionary(child => child.ExecutableNodeId, StringComparer.Ordinal);
        var structure = ReadStructure(executableNode);
        var elements = structure?.Elements ?? [];
        var flows = structure?.SequenceFlows ?? [];
        var variableNames = (structure?.Variables ?? []).Select(variable => variable.Name).ToHashSet(StringComparer.Ordinal);
        var isTransaction = structure?.IsTransaction ?? false;

        Validate(elements, flows, childrenByNodeId, variableNames, isTransaction);

        return new BpmnGraph(elements, flows, childrenByNodeId, isTransaction);
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

    /// <summary>The timer/message/signal catch boundary events attached to <paramref name="hostElementId"/> (spec 120); these arm a suspending listener child when the host is scheduled. Error, compensation, and cancel boundaries arm nothing (spec 120/124/125).</summary>
    public IReadOnlyCollection<BpmnElement> AttachedCatchBoundaries(string hostElementId) =>
        _boundariesByHost[hostElementId]
            .Where(boundary => !BpmnElementFamilies.IsErrorBoundary(boundary) && !BpmnElementFamilies.IsCompensationBoundary(boundary) && !BpmnElementFamilies.IsCancelBoundary(boundary))
            .ToArray();

    /// <summary>The single error boundary attached to <paramref name="hostElementId"/> (spec 120), or <c>null</c> when the host has none; validation caps a host at one error boundary.</summary>
    public BpmnElement? AttachedErrorBoundary(string hostElementId) =>
        _boundariesByHost[hostElementId].FirstOrDefault(BpmnElementFamilies.IsErrorBoundary);

    /// <summary>The single compensation boundary attached to <paramref name="hostElementId"/> (spec 124), or <c>null</c> when the host has none; validation caps a host at one compensation boundary.</summary>
    public BpmnElement? AttachedCompensationBoundary(string hostElementId) =>
        _boundariesByHost[hostElementId].FirstOrDefault(BpmnElementFamilies.IsCompensationBoundary);

    /// <summary>The single cancel boundary attached to <paramref name="hostElementId"/> (spec 125), or <c>null</c> when the transaction host has none; validation caps a transaction host at one cancel boundary.</summary>
    public BpmnElement? AttachedCancelBoundary(string hostElementId) =>
        _boundariesByHost[hostElementId].FirstOrDefault(BpmnElementFamilies.IsCancelBoundary);

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

    /// <summary>
    /// Whether <paramref name="flowId"/> is a backward (loop-back) sequence flow (spec 122): a token that
    /// traverses it mints a fresh loop-iteration key rather than inheriting the emitting token's. The set is
    /// precomputed at construction (<see cref="ComputeBackwardFlowIds"/>) and is a deterministic function of
    /// the element/flow/start-event sets.
    /// </summary>
    public bool IsBackwardFlow(string flowId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flowId);
        return _backwardFlowIds.Contains(flowId);
    }

    /// <summary>
    /// Classifies the graph's backward (loop-closing) sequence flows once at construction (spec 122). A
    /// backward edge is the standard compiler back edge: during a depth-first traversal from the start-event
    /// roots, an edge <c>u → v</c> is backward iff <c>v</c> is GRAY — on the current DFS stack, an ancestor of
    /// <c>u</c> — when the edge is examined. This marks exactly the loop-closing edge of each loop and never a
    /// forward/cross edge of the cycle (unlike the naive "target can reach source", which marks every edge of
    /// a cycle). Roots are the start events, then any remaining unvisited elements; both root lists and each
    /// node's outbound flows are ordinal-sorted, so the result is stable regardless of authoring order and of
    /// multi-start iteration order.
    /// </summary>
    private static IReadOnlySet<string> ComputeBackwardFlowIds(
        IReadOnlyCollection<BpmnElement> elements,
        ILookup<string, BpmnSequenceFlow> outboundBySource,
        IReadOnlyCollection<BpmnElement> startEvents)
    {
        const int gray = 1;
        const int black = 2;

        var backward = new HashSet<string>(StringComparer.Ordinal);
        var color = new Dictionary<string, int>(StringComparer.Ordinal);

        var roots = startEvents.Select(element => element.ElementId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .Concat(elements.Select(element => element.ElementId).OrderBy(id => id, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal);

        foreach (var root in roots)
            Visit(root);

        return backward;

        void Visit(string elementId)
        {
            color[elementId] = gray;

            foreach (var flow in outboundBySource[elementId].OrderBy(flow => flow.FlowId, StringComparer.Ordinal))
            {
                var known = color.TryGetValue(flow.TargetRef, out var state) ? state : 0;
                if (known == gray)
                    backward.Add(flow.FlowId); // target is on the current DFS stack — a loop-closing edge.
                else if (known != black)
                    Visit(flow.TargetRef);
            }

            color[elementId] = black;
        }
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
        IReadOnlyDictionary<string, ExecutableNode> childrenByNodeId,
        IReadOnlySet<string> declaredVariableNames,
        bool structureIsTransaction)
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

        ValidateBoundaryEvents(elements, outboundBySource, inboundByTarget);

        ValidateCompensation(elements, outboundBySource, inboundByTarget);

        ValidateTransaction(elements, structureIsTransaction);

        ValidateMultiInstance(elements, declaredVariableNames);

        // Cyclic sequence flows are executable (spec 122): loop-back edges become loop-iteration keys. The
        // structural rules above still constrain where a loop-back may land — a loop-back into a start event
        // (no inbound), a boundary event (no inbound), or an event-gateway-armed catch (exactly-one-inbound)
        // is rejected by those rules — so no acyclicity check remains.
    }

    /// <summary>
    /// Multi-instance loop rules (spec 121 D1): loop characteristics are valid only on a task-family or
    /// <c>subProcess</c> host that binds a child; exactly one of cardinality (≥ 1) XOR collection variable is
    /// set; a collection variable must name a declared container-scoped variable. Collection mode is a stated
    /// cut in this slice (not executable) and is rejected — the follow-up unit that adds the container-variable
    /// read seam removes this rule.
    /// </summary>
    private static void ValidateMultiInstance(IReadOnlyCollection<BpmnElement> elements, IReadOnlySet<string> declaredVariableNames)
    {
        foreach (var element in elements)
        {
            if (element.LoopCharacteristics is not { } loop)
                continue;

            if (!BpmnElementFamilies.IsBoundaryHostFamily(element) || element.ChildNodeId is null)
                throw new BpmnExecutionException($"BPMN element '{element.ElementId}' ({element.ElementType}) declares multi-instance loop characteristics but is not a task-family or subprocess host with a bound child.");

            var hasCardinality = loop.Cardinality is not null;
            var hasCollection = loop.CollectionVariable is not null;
            if (hasCardinality == hasCollection)
                throw new BpmnExecutionException($"BPMN multi-instance element '{element.ElementId}' must declare exactly one of a positive cardinality or a collection variable.");

            if (hasCardinality && loop.Cardinality < 1)
                throw new BpmnExecutionException($"BPMN multi-instance element '{element.ElementId}' declares a cardinality of {loop.Cardinality}; the cardinality must be a positive integer.");

            if (hasCollection && !declaredVariableNames.Contains(loop.CollectionVariable!))
                throw new BpmnExecutionException($"BPMN multi-instance element '{element.ElementId}' names collection variable '{loop.CollectionVariable}', which is not a declared container-scoped variable of the process.");

            // The item key must not collide with the reserved loopIndex key the iteration frame always seeds
            // (spec 123 D2) — a collision would silently overwrite one with the other.
            if (hasCollection && StringComparer.Ordinal.Equals(loop.ItemVariable, BpmnLoopCharacteristics.LoopIndexVariable))
                throw new BpmnExecutionException($"BPMN multi-instance element '{element.ElementId}' uses item variable '{BpmnLoopCharacteristics.LoopIndexVariable}', which is reserved for the zero-based iteration index; choose a different item variable.");
        }
    }

    /// <summary>
    /// Boundary event rules (spec 120 D2): the <c>attachedToRef</c> host must exist, be a task-family or
    /// subprocess element, and have a bound child; a boundary takes no inbound flows and at least one
    /// outbound flow; a catch boundary (timer/message/signal) binds a listener child while an error
    /// boundary binds none and must be interrupting; a host carries at most one error boundary; a boundary
    /// declares no default flow (its outbound is taken unconditionally when it fires).
    /// </summary>
    private static void ValidateBoundaryEvents(
        IReadOnlyCollection<BpmnElement> elements,
        ILookup<string, BpmnSequenceFlow> outboundBySource,
        ILookup<string, BpmnSequenceFlow> inboundByTarget)
    {
        var elementsById = elements.ToDictionary(element => element.ElementId, StringComparer.Ordinal);
        var errorBoundaryCountByHost = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var boundary in elements.Where(element => StringComparer.Ordinal.Equals(element.ElementType, BpmnElementTypes.BoundaryEvent)))
        {
            if (boundary.AttachedToRef is not { } hostId)
                throw new BpmnExecutionException($"BPMN boundary event '{boundary.ElementId}' must declare an attachedToRef host element.");

            if (!elementsById.TryGetValue(hostId, out var host))
                throw new BpmnExecutionException($"BPMN boundary event '{boundary.ElementId}' is attached to '{hostId}', which does not exist.");

            if (!BpmnElementFamilies.IsBoundaryHostFamily(host))
                throw new BpmnExecutionException($"BPMN boundary event '{boundary.ElementId}' is attached to '{hostId}' ({host.ElementType}), which is not a task-family or subprocess host.");

            if (host.ChildNodeId is null)
                throw new BpmnExecutionException($"BPMN boundary event '{boundary.ElementId}' is attached to host '{hostId}', which has no bound child activity; a boundary event can only attach to a host that runs a child.");

            if (inboundByTarget[boundary.ElementId].Any())
                throw new BpmnExecutionException($"BPMN boundary event '{boundary.ElementId}' cannot have inbound sequence flows; it is entered by its host's activation.");

            // spec 124: a compensation boundary inverts the outbound rule — it has zero outbound flows (its
            // "outbound" is the association to its handler); every other boundary needs at least one.
            var isCompensationBoundary = BpmnElementFamilies.IsCompensationBoundary(boundary);
            if (isCompensationBoundary)
            {
                if (outboundBySource[boundary.ElementId].Any())
                    throw new BpmnExecutionException($"BPMN compensation boundary event '{boundary.ElementId}' cannot have outbound sequence flows; its handler is reached by association, not by token flow.");
            }
            else if (!outboundBySource[boundary.ElementId].Any())
            {
                throw new BpmnExecutionException($"BPMN boundary event '{boundary.ElementId}' must have at least one outbound sequence flow.");
            }

            if (boundary.DefaultFlowId is not null || outboundBySource[boundary.ElementId].Any(flow => flow.IsDefault))
                throw new BpmnExecutionException($"BPMN boundary event '{boundary.ElementId}' cannot declare a default sequence flow; its outbound is taken unconditionally when it fires.");

            if (isCompensationBoundary)
            {
                // spec 124 rule 1: a compensation boundary binds no listener child (CancelActivity is ignored);
                // its host must not carry loop characteristics (multi-instance host compensation is a stated cut).
                // The handler reference is validated in ValidateCompensation.
                if (boundary.ChildNodeId is not null)
                    throw new BpmnExecutionException($"BPMN compensation boundary event '{boundary.ElementId}' cannot bind a child activity; it has no listener and fires after its host completes.");
                if (host.LoopCharacteristics is not null)
                    throw new BpmnExecutionException($"BPMN compensation boundary event '{boundary.ElementId}' is attached to multi-instance host '{hostId}'; compensation on a multi-instance host is not supported by this slice.");
            }
            else if (BpmnElementFamilies.IsErrorBoundary(boundary))
            {
                if (boundary.ChildNodeId is not null)
                    throw new BpmnExecutionException($"BPMN error boundary event '{boundary.ElementId}' cannot bind a child activity; it absorbs the host's child fault and has no listener.");
                if (!boundary.CancelActivity)
                    throw new BpmnExecutionException($"BPMN error boundary event '{boundary.ElementId}' must be interrupting (cancelActivity=true); a non-interrupting error boundary is not meaningful.");

                errorBoundaryCountByHost[hostId] = (errorBoundaryCountByHost.TryGetValue(hostId, out var count) ? count : 0) + 1;
                if (errorBoundaryCountByHost[hostId] > 1)
                    throw new BpmnExecutionException($"BPMN host '{hostId}' declares more than one error boundary event; without error-code matching a host may carry at most one.");
            }
            else if (BpmnElementFamilies.IsCancelBoundary(boundary))
            {
                // spec 125: a cancel boundary is dormant (no listener child, like error/compensation) and routes its
                // outbound flows (≥1, already enforced above) when the transaction completes Cancelled. CancelActivity
                // is ignored (the host is already finished when it fires). The host-is-a-transaction and
                // at-most-one-per-host rules live in ValidateTransaction.
                if (boundary.ChildNodeId is not null)
                    throw new BpmnExecutionException($"BPMN cancel boundary event '{boundary.ElementId}' cannot bind a child activity; it is dormant and fires on the transaction's Cancelled outcome.");
            }
            else if (boundary.ChildNodeId is null)
            {
                throw new BpmnExecutionException($"BPMN catch boundary event '{boundary.ElementId}' requires a bound suspending listener child (a Delay for timer, an Event for message/signal).");
            }
        }
    }

    /// <summary>
    /// Compensation rules (spec 124 D1). Compensation boundaries reference an existing handler; handlers are
    /// task-family/subProcess elements that bind a child, take zero sequence flows, carry no loop characteristics,
    /// host no boundary, and are referenced by exactly one compensation boundary (orphans rejected); compensate
    /// throw events bind no child; an <c>activityRef</c> on a compensate throw/end names an existing element with
    /// an attached compensation boundary; and handlers never sit at a start/boundary-host/throw slot. Each rule
    /// rejects deterministically with the offending element named.
    /// </summary>
    private static void ValidateCompensation(
        IReadOnlyCollection<BpmnElement> elements,
        ILookup<string, BpmnSequenceFlow> outboundBySource,
        ILookup<string, BpmnSequenceFlow> inboundByTarget)
    {
        var elementsById = elements.ToDictionary(element => element.ElementId, StringComparer.Ordinal);
        var boundariesByHandler = elements
            .Where(BpmnElementFamilies.IsCompensationBoundary)
            .Where(boundary => boundary.CompensationHandlerElementId is not null)
            .ToLookup(boundary => boundary.CompensationHandlerElementId!, StringComparer.Ordinal);
        var attachedByHost = elements
            .Where(element => StringComparer.Ordinal.Equals(element.ElementType, BpmnElementTypes.BoundaryEvent) && element.AttachedToRef is not null)
            .ToLookup(element => element.AttachedToRef!, StringComparer.Ordinal);

        // Rule 1 — a compensation boundary references an existing handler.
        foreach (var boundary in elements.Where(BpmnElementFamilies.IsCompensationBoundary))
        {
            if (boundary.CompensationHandlerElementId is not { } handlerId)
                throw new BpmnExecutionException($"BPMN compensation boundary event '{boundary.ElementId}' must reference a compensation handler via its compensation-handler element id.");
            if (!elementsById.TryGetValue(handlerId, out var referenced))
                throw new BpmnExecutionException($"BPMN compensation boundary event '{boundary.ElementId}' references handler '{handlerId}', which does not exist.");
            if (!referenced.IsForCompensation)
                throw new BpmnExecutionException($"BPMN compensation boundary event '{boundary.ElementId}' references '{handlerId}', which is not a compensation handler element (isForCompensation).");
        }

        // Rule 2 — handler shape (task-family/subProcess, binds child, zero flows, no loop characteristics, hosts
        // no boundary, referenced by exactly one compensation boundary; orphans rejected).
        foreach (var handler in elements.Where(element => element.IsForCompensation))
        {
            if (!BpmnElementFamilies.IsBoundaryHostFamily(handler))
                throw new BpmnExecutionException($"BPMN compensation handler '{handler.ElementId}' ({handler.ElementType}) must be a task-family or subprocess element.");
            if (handler.ChildNodeId is null)
                throw new BpmnExecutionException($"BPMN compensation handler '{handler.ElementId}' must bind a child activity.");
            if (inboundByTarget[handler.ElementId].Any() || outboundBySource[handler.ElementId].Any())
                throw new BpmnExecutionException($"BPMN compensation handler '{handler.ElementId}' cannot participate in sequence flows; it is invoked only by the compensation replay.");
            if (handler.LoopCharacteristics is not null)
                throw new BpmnExecutionException($"BPMN compensation handler '{handler.ElementId}' cannot carry loop characteristics.");
            if (attachedByHost[handler.ElementId].Any())
                throw new BpmnExecutionException($"BPMN compensation handler '{handler.ElementId}' cannot host attached boundary events.");

            var references = boundariesByHandler[handler.ElementId].Count();
            if (references == 0)
                throw new BpmnExecutionException($"BPMN compensation handler '{handler.ElementId}' is referenced by no compensation boundary (orphan handler); it cannot ride normal flow.");
            if (references > 1)
                throw new BpmnExecutionException($"BPMN compensation handler '{handler.ElementId}' is referenced by more than one compensation boundary; a handler binds exactly one boundary.");
        }

        // Rule 3 — a handler never sits at a start-event slot (its zero-flows rule already blocks it from
        // gateway targets and boundary-host slots, which are reached/entered by flows or activation).
        foreach (var start in elements.Where(element => StringComparer.Ordinal.Equals(element.ElementType, BpmnElementTypes.StartEvent) && element.IsForCompensation))
            throw new BpmnExecutionException($"BPMN start event '{start.ElementId}' cannot be a compensation handler.");

        // Rule 4 — a compensation-handler reference belongs only on a compensation boundary, and a compensate
        // throw event binds no child.
        foreach (var element in elements.Where(element => element.CompensationHandlerElementId is not null && !BpmnElementFamilies.IsCompensationBoundary(element)))
            throw new BpmnExecutionException($"BPMN element '{element.ElementId}' declares a compensation-handler reference but is not a compensation boundary event.");

        foreach (var throwEvent in elements.Where(element => StringComparer.Ordinal.Equals(element.ElementType, BpmnElementTypes.IntermediateThrowEvent)))
        {
            if (throwEvent.ChildNodeId is not null)
                throw new BpmnExecutionException($"BPMN compensate throw event '{throwEvent.ElementId}' cannot bind a child activity.");
        }

        // Rule 5 — an activityRef on a compensate throw/end names an existing element with an attached
        // compensation boundary in this process.
        var compensationHosts = elements
            .Where(BpmnElementFamilies.IsCompensationBoundary)
            .Where(boundary => boundary.AttachedToRef is not null)
            .Select(boundary => boundary.AttachedToRef!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var thrower in elements.Where(IsCompensateThrowOrEnd))
        {
            var activityRef = thrower.EventDefinitions.Single().Properties.TryGetValue(BpmnEventDefinitionProperties.ActivityRef, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : null;
            if (activityRef is null)
                continue;
            if (!elementsById.ContainsKey(activityRef))
                throw new BpmnExecutionException($"BPMN compensate event '{thrower.ElementId}' targets activityRef '{activityRef}', which does not exist in this process.");
            if (!compensationHosts.Contains(activityRef))
                throw new BpmnExecutionException($"BPMN compensate event '{thrower.ElementId}' targets activityRef '{activityRef}', which has no attached compensation boundary.");
        }
    }

    /// <summary>
    /// Transaction rules (spec 125 D1). An <c>IsTransaction</c> element must be a <c>subProcess</c>-family element
    /// with a bound child and must not carry loop characteristics (MI transactions are a stated cut). A cancel end
    /// event is valid only inside a transaction structure. A cancel boundary may only attach to an
    /// <c>IsTransaction</c> host, and a transaction host carries at most one cancel boundary. Each rule rejects
    /// deterministically with the offending element named.
    /// </summary>
    private static void ValidateTransaction(IReadOnlyCollection<BpmnElement> elements, bool structureIsTransaction)
    {
        var elementsById = elements.ToDictionary(element => element.ElementId, StringComparer.Ordinal);

        // Rule 1 / Rule 4 — an IsTransaction element is a subProcess with a bound child and no loop characteristics.
        foreach (var element in elements.Where(element => element.IsTransaction))
        {
            if (!StringComparer.Ordinal.Equals(element.ElementType, BpmnElementTypes.SubProcess))
                throw new BpmnExecutionException($"BPMN element '{element.ElementId}' ({element.ElementType}) is marked as a transaction, but only a subprocess element may be a transaction.");
            if (element.ChildNodeId is null)
                throw new BpmnExecutionException($"BPMN transaction '{element.ElementId}' requires a bound child activity (the nested transaction process).");
            if (element.LoopCharacteristics is not null)
                throw new BpmnExecutionException($"BPMN transaction '{element.ElementId}' cannot carry multi-instance loop characteristics; multi-instance transactions are not supported by this slice.");
        }

        // Rule 2 — a cancel end event is valid only inside a transaction structure.
        foreach (var cancelEnd in elements.Where(BpmnElementFamilies.IsCancelEndEvent))
        {
            if (!structureIsTransaction)
                throw new BpmnExecutionException($"BPMN cancel end event '{cancelEnd.ElementId}' is only valid inside a transaction; this process is not a transaction.");
        }

        // Rule 2/3 — a cancel boundary attaches only to a transaction host, at most one per host.
        var cancelBoundaryCountByHost = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var boundary in elements.Where(BpmnElementFamilies.IsCancelBoundary))
        {
            // ValidateBoundaryEvents already rejects a missing/unresolvable host, so a resolvable host is expected here.
            if (boundary.AttachedToRef is not { } hostId || !elementsById.TryGetValue(hostId, out var host))
                continue;
            if (!host.IsTransaction)
                throw new BpmnExecutionException($"BPMN cancel boundary event '{boundary.ElementId}' is attached to '{hostId}', which is not a transaction; a cancel boundary may only attach to a transaction.");

            cancelBoundaryCountByHost[hostId] = (cancelBoundaryCountByHost.TryGetValue(hostId, out var count) ? count : 0) + 1;
            if (cancelBoundaryCountByHost[hostId] > 1)
                throw new BpmnExecutionException($"BPMN transaction '{hostId}' declares more than one cancel boundary event; a transaction host may carry at most one.");
        }
    }

    private static bool IsCompensateThrowOrEnd(BpmnElement element) =>
        (StringComparer.Ordinal.Equals(element.ElementType, BpmnElementTypes.IntermediateThrowEvent) ||
         StringComparer.Ordinal.Equals(element.ElementType, BpmnElementTypes.EndEvent)) &&
        BpmnElementFamilies.HasCompensateDefinition(element);

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

}
