using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Activities.Bpmn.Models;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>
/// Maps a <see cref="BpmnElement"/> to the behavior family that executes it. Families are the
/// registration keys of <c>IBpmnBehaviorRegistry</c>; the whole task family shares one behavior because
/// the task subtype only changes which Elsa child activity is bound, never the token semantics.
/// </summary>
public static class BpmnElementFamilies
{
    public const string StartEventNone = "startEvent.none";
    public const string StartEventTimer = "startEvent.timer";
    public const string StartEventMessage = "startEvent.message";
    public const string StartEventSignal = "startEvent.signal";

    /// <summary>The escalation event-subprocess body start family (spec 128): seeds one token via the scheduled-start hint, then routes outbound like a none start. Never externally triggered.</summary>
    public const string StartEventEscalation = "startEvent.escalation";

    /// <summary>The error event-subprocess body start family (spec 128): seeds one token via the scheduled-start hint, then routes outbound like a none start. Never externally triggered.</summary>
    public const string StartEventError = "startEvent.error";
    public const string EndEventNone = "endEvent.none";
    public const string EndEventTerminate = "endEvent.terminate";

    /// <summary>The compensate end event family (spec 124): triggers a compensation replay, then consumes its token (none-end semantics).</summary>
    public const string EndEventCompensation = "endEvent.compensation";

    /// <summary>The cancel end event family (spec 125): cancels the enclosing transaction — stop other live work, replay the scope's compensables, then complete with the <c>Cancelled</c> outcome.</summary>
    public const string EndEventCancel = "endEvent.cancel";
    public const string IntermediateCatchEvent = "intermediateCatchEvent.catch";

    /// <summary>The compensate intermediate throw event family (spec 124): triggers a compensation replay, then routes its outbound flows.</summary>
    public const string IntermediateThrowEventCompensation = "intermediateThrowEvent.compensation";

    /// <summary>The escalation intermediate throw event family (spec 127): raises an escalation to the parent scope, then routes its outbound flows (fire-and-continue).</summary>
    public const string IntermediateThrowEventEscalation = "intermediateThrowEvent.escalation";

    /// <summary>The escalation end event family (spec 127): raises an escalation to the parent scope, then consumes its token (none-end semantics).</summary>
    public const string EndEventEscalation = "endEvent.escalation";
    public const string Task = "task";
    public const string SubProcess = "subProcess";
    public const string ExclusiveGateway = "exclusiveGateway";
    public const string ParallelGateway = "parallelGateway";
    public const string InclusiveGateway = "inclusiveGateway";
    public const string EventBasedGateway = "eventBasedGateway";

    /// <summary>The single behavior family for every boundary event (spec 120); catch vs error is a per-element definition detail, not a separate behavior.</summary>
    public const string BoundaryEvent = "boundaryEvent";

    private static readonly HashSet<string> TaskElementTypes = new(StringComparer.Ordinal)
    {
        BpmnElementTypes.Task,
        BpmnElementTypes.UserTask,
        BpmnElementTypes.ServiceTask,
        BpmnElementTypes.ScriptTask,
        BpmnElementTypes.ManualTask,
        BpmnElementTypes.BusinessRuleTask,
        BpmnElementTypes.SendTask,
        BpmnElementTypes.ReceiveTask
    };

    public static string Resolve(BpmnElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (TaskElementTypes.Contains(element.ElementType))
            return Task;

        return element.ElementType switch
        {
            BpmnElementTypes.StartEvent => ResolveStartEvent(element),
            BpmnElementTypes.EndEvent => ResolveEndEvent(element),
            BpmnElementTypes.IntermediateCatchEvent => ResolveIntermediateCatchEvent(element),
            BpmnElementTypes.IntermediateThrowEvent => ResolveIntermediateThrowEvent(element),
            BpmnElementTypes.SubProcess => SubProcess,
            BpmnElementTypes.ExclusiveGateway => ExclusiveGateway,
            BpmnElementTypes.ParallelGateway => ParallelGateway,
            BpmnElementTypes.InclusiveGateway => InclusiveGateway,
            BpmnElementTypes.EventBasedGateway => EventBasedGateway,
            BpmnElementTypes.BoundaryEvent => ResolveBoundaryEvent(element),
            _ => throw new BpmnExecutionException(
                $"BPMN element '{element.ElementId}' has element type '{element.ElementType}', which this engine slice does not support.")
        };
    }

    /// <summary>
    /// The event-definition types an intermediate catch event may declare in this engine slice
    /// (spec 116). The definition type is authoring/interchange semantics: every catch event waits
    /// through its bound suspending child, so the runtime family is the same for all three.
    /// </summary>
    private static readonly HashSet<string> SupportedCatchEventDefinitionTypes = new(StringComparer.Ordinal)
    {
        BpmnEventDefinitionTypes.Timer,
        BpmnEventDefinitionTypes.Message,
        BpmnEventDefinitionTypes.Signal
    };

    /// <summary>
    /// The event-definition types a boundary event may declare (spec 120): the three listener kinds
    /// (timer/message/signal, which arm a suspending child) plus error (which absorbs the host's child
    /// fault). Escalation/compensation boundaries are a later unit.
    /// </summary>
    private static readonly HashSet<string> SupportedBoundaryDefinitionTypes = new(StringComparer.Ordinal)
    {
        BpmnEventDefinitionTypes.Timer,
        BpmnEventDefinitionTypes.Message,
        BpmnEventDefinitionTypes.Signal,
        BpmnEventDefinitionTypes.Error,
        BpmnEventDefinitionTypes.Escalation,
        BpmnEventDefinitionTypes.Compensation,
        BpmnEventDefinitionTypes.Cancel
    };

    private static string ResolveBoundaryEvent(BpmnElement element)
    {
        if (element.EventDefinitions.Count != 1)
            throw new BpmnExecutionException(
                $"BPMN boundary event '{element.ElementId}' must declare exactly one event definition; it declares {element.EventDefinitions.Count}.");

        var definitionType = element.EventDefinitions.Single().Type;
        if (!SupportedBoundaryDefinitionTypes.Contains(definitionType))
            throw new BpmnExecutionException(
                $"BPMN boundary event '{element.ElementId}' declares event definition type '{definitionType}'; only timer, message, signal, error, escalation, compensation, and cancel boundary events are supported by this engine slice.");

        return BoundaryEvent;
    }

    /// <summary>True when a <c>boundaryEvent</c> is an escalation boundary (spec 127): dormant (no listener child), notification-driven, routes its outbound flows.</summary>
    public static bool IsEscalationBoundary(BpmnElement element) =>
        StringComparer.Ordinal.Equals(element.ElementType, BpmnElementTypes.BoundaryEvent) &&
        element.EventDefinitions.Count == 1 &&
        StringComparer.Ordinal.Equals(element.EventDefinitions.Single().Type, BpmnEventDefinitionTypes.Escalation);

    /// <summary>True when an element is an escalation throw (intermediate) or escalation end event (spec 127): it carries exactly one escalation event definition.</summary>
    public static bool IsEscalationThrowOrEnd(BpmnElement element) =>
        (StringComparer.Ordinal.Equals(element.ElementType, BpmnElementTypes.IntermediateThrowEvent) ||
         StringComparer.Ordinal.Equals(element.ElementType, BpmnElementTypes.EndEvent)) &&
        element.EventDefinitions.Count == 1 &&
        StringComparer.Ordinal.Equals(element.EventDefinitions.Single().Type, BpmnEventDefinitionTypes.Escalation);

    /// <summary>True when a <c>boundaryEvent</c> is an error boundary (absorbs the host's child fault, no listener child); false when it is a timer/message/signal catch boundary.</summary>
    public static bool IsErrorBoundary(BpmnElement element) =>
        StringComparer.Ordinal.Equals(element.ElementType, BpmnElementTypes.BoundaryEvent) &&
        element.EventDefinitions.Count == 1 &&
        StringComparer.Ordinal.Equals(element.EventDefinitions.Single().Type, BpmnEventDefinitionTypes.Error);

    /// <summary>True when a <c>boundaryEvent</c> is a compensation boundary (spec 124): it is dormant (no listener, no outbound flows) and its handler is reached by association, not by token flow.</summary>
    public static bool IsCompensationBoundary(BpmnElement element) =>
        StringComparer.Ordinal.Equals(element.ElementType, BpmnElementTypes.BoundaryEvent) &&
        element.EventDefinitions.Count == 1 &&
        StringComparer.Ordinal.Equals(element.EventDefinitions.Single().Type, BpmnEventDefinitionTypes.Compensation);

    /// <summary>True when an element carries a compensate event definition (spec 124): a compensate throw or a compensate end event.</summary>
    public static bool HasCompensateDefinition(BpmnElement element) =>
        element.EventDefinitions.Count == 1 &&
        StringComparer.Ordinal.Equals(element.EventDefinitions.Single().Type, BpmnEventDefinitionTypes.Compensation);

    /// <summary>True when an element is a cancel end event (spec 125): an <c>endEvent</c> whose single event definition is <see cref="BpmnEventDefinitionTypes.Cancel"/>.</summary>
    public static bool IsCancelEndEvent(BpmnElement element) =>
        StringComparer.Ordinal.Equals(element.ElementType, BpmnElementTypes.EndEvent) &&
        element.EventDefinitions.Count == 1 &&
        StringComparer.Ordinal.Equals(element.EventDefinitions.Single().Type, BpmnEventDefinitionTypes.Cancel);

    /// <summary>True when a <c>boundaryEvent</c> is a cancel boundary (spec 125): dormant (no listener), fires on the transaction's <c>Cancelled</c> outcome and routes its outbound flows.</summary>
    public static bool IsCancelBoundary(BpmnElement element) =>
        StringComparer.Ordinal.Equals(element.ElementType, BpmnElementTypes.BoundaryEvent) &&
        element.EventDefinitions.Count == 1 &&
        StringComparer.Ordinal.Equals(element.EventDefinitions.Single().Type, BpmnEventDefinitionTypes.Cancel);

    /// <summary>The host families a boundary event may attach to (spec 120 D2): the task family and embedded subprocesses.</summary>
    public static bool IsBoundaryHostFamily(BpmnElement element) =>
        TaskElementTypes.Contains(element.ElementType) ||
        StringComparer.Ordinal.Equals(element.ElementType, BpmnElementTypes.SubProcess);

    private static string ResolveIntermediateCatchEvent(BpmnElement element)
    {
        if (element.EventDefinitions.Count != 1)
            throw new BpmnExecutionException(
                $"BPMN intermediate catch event '{element.ElementId}' must declare exactly one event definition; it declares {element.EventDefinitions.Count}.");

        var definitionType = element.EventDefinitions.Single().Type;
        if (!SupportedCatchEventDefinitionTypes.Contains(definitionType))
            throw new BpmnExecutionException(
                $"BPMN intermediate catch event '{element.ElementId}' declares event definition type '{definitionType}'; only timer, message, and signal catch events are supported by this engine slice.");

        return IntermediateCatchEvent;
    }

    /// <summary>
    /// The event-defined start families (spec 117), keyed by event-definition type. A start event declaring
    /// exactly one timer/message/signal definition registers a durable start trigger at publish time and seeds a
    /// single token at runtime — the trigger machinery is entirely publish/dispatch-time; the runtime token
    /// behavior equals a none start.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> EventStartFamiliesByDefinitionType =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [BpmnEventDefinitionTypes.Timer] = StartEventTimer,
            [BpmnEventDefinitionTypes.Message] = StartEventMessage,
            [BpmnEventDefinitionTypes.Signal] = StartEventSignal
        };

    /// <summary>The event-start families that seed at publish/dispatch time (all four route outbound like a none start).</summary>
    public static readonly IReadOnlySet<string> StartEventFamilies =
        new HashSet<string>(StringComparer.Ordinal) { StartEventNone, StartEventTimer, StartEventMessage, StartEventSignal };

    private static string ResolveStartEvent(BpmnElement element)
    {
        if (element.EventDefinitions.Count == 0)
            return StartEventNone;

        if (element.EventDefinitions.Count == 1)
        {
            var type = element.EventDefinitions.Single().Type;
            if (EventStartFamiliesByDefinitionType.TryGetValue(type, out var family))
                return family;
            // spec 128: an escalation/error start event is an event-subprocess body start (seeded via the scheduled-start
            // hint). Its runtime token behavior equals a none start; it is never a publish-time start trigger.
            if (StringComparer.Ordinal.Equals(type, BpmnEventDefinitionTypes.Escalation))
                return StartEventEscalation;
            if (StringComparer.Ordinal.Equals(type, BpmnEventDefinitionTypes.Error))
                return StartEventError;
        }

        throw new BpmnExecutionException(
            $"BPMN start event '{element.ElementId}' declares unsupported event definitions; only none, timer, message, signal, and event-subprocess (escalation/error) start events are supported by this engine slice (exactly one such definition).");
    }

    /// <summary>
    /// True when a start event is externally triggered at publish/dispatch time (spec 117): a timer/message/signal
    /// start. A none start is direct-invocation, and an escalation/error start (spec 128) is an event-subprocess
    /// body start seeded via the scheduled-start hint — neither registers a publish-time start trigger.
    /// </summary>
    public static bool IsExternalStartTrigger(BpmnElement element) =>
        StringComparer.Ordinal.Equals(element.ElementType, BpmnElementTypes.StartEvent) &&
        element.EventDefinitions.Count == 1 &&
        EventStartFamiliesByDefinitionType.ContainsKey(element.EventDefinitions.Single().Type);

    private static string ResolveEndEvent(BpmnElement element)
    {
        if (element.EventDefinitions.Count == 0)
            return EndEventNone;

        if (element.EventDefinitions.Count == 1)
        {
            var type = element.EventDefinitions.Single().Type;
            if (StringComparer.Ordinal.Equals(type, BpmnEventDefinitionTypes.Terminate))
                return EndEventTerminate;
            if (StringComparer.Ordinal.Equals(type, BpmnEventDefinitionTypes.Compensation))
                return EndEventCompensation;
            if (StringComparer.Ordinal.Equals(type, BpmnEventDefinitionTypes.Cancel))
                return EndEventCancel;
            if (StringComparer.Ordinal.Equals(type, BpmnEventDefinitionTypes.Escalation))
                return EndEventEscalation;
        }

        throw new BpmnExecutionException(
            $"BPMN end event '{element.ElementId}' declares unsupported event definitions; only none, terminate, compensate, cancel, and escalation end events are supported by this engine slice.");
    }

    /// <summary>
    /// Resolves an intermediate throw event. This slice wires two definitions: exactly one
    /// <see cref="BpmnEventDefinitionTypes.Compensation"/> definition → the compensate throw family (spec 124);
    /// exactly one <see cref="BpmnEventDefinitionTypes.Escalation"/> definition → the escalation throw family
    /// (spec 127); any other (or no) definition is rejected.
    /// </summary>
    private static string ResolveIntermediateThrowEvent(BpmnElement element)
    {
        if (element.EventDefinitions.Count != 1)
            throw new BpmnExecutionException(
                $"BPMN intermediate throw event '{element.ElementId}' must declare exactly one event definition; it declares {element.EventDefinitions.Count}.");

        var definitionType = element.EventDefinitions.Single().Type;
        if (StringComparer.Ordinal.Equals(definitionType, BpmnEventDefinitionTypes.Compensation))
            return IntermediateThrowEventCompensation;
        if (StringComparer.Ordinal.Equals(definitionType, BpmnEventDefinitionTypes.Escalation))
            return IntermediateThrowEventEscalation;

        throw new BpmnExecutionException(
            $"BPMN intermediate throw event '{element.ElementId}' declares event definition type '{definitionType}'; only compensate and escalation throw events are supported by this engine slice.");
    }
}
