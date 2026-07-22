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
    public const string EndEventNone = "endEvent.none";
    public const string EndEventTerminate = "endEvent.terminate";
    public const string IntermediateCatchEvent = "intermediateCatchEvent.catch";
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
        BpmnEventDefinitionTypes.Error
    };

    private static string ResolveBoundaryEvent(BpmnElement element)
    {
        if (element.EventDefinitions.Count != 1)
            throw new BpmnExecutionException(
                $"BPMN boundary event '{element.ElementId}' must declare exactly one event definition; it declares {element.EventDefinitions.Count}.");

        var definitionType = element.EventDefinitions.Single().Type;
        if (!SupportedBoundaryDefinitionTypes.Contains(definitionType))
            throw new BpmnExecutionException(
                $"BPMN boundary event '{element.ElementId}' declares event definition type '{definitionType}'; only timer, message, signal, and error boundary events are supported by this engine slice.");

        return BoundaryEvent;
    }

    /// <summary>True when a <c>boundaryEvent</c> is an error boundary (absorbs the host's child fault, no listener child); false when it is a timer/message/signal catch boundary.</summary>
    public static bool IsErrorBoundary(BpmnElement element) =>
        StringComparer.Ordinal.Equals(element.ElementType, BpmnElementTypes.BoundaryEvent) &&
        element.EventDefinitions.Count == 1 &&
        StringComparer.Ordinal.Equals(element.EventDefinitions.Single().Type, BpmnEventDefinitionTypes.Error);

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

        if (element.EventDefinitions.Count == 1 &&
            EventStartFamiliesByDefinitionType.TryGetValue(element.EventDefinitions.Single().Type, out var family))
            return family;

        throw new BpmnExecutionException(
            $"BPMN start event '{element.ElementId}' declares unsupported event definitions; only none, timer, message, and signal start events are supported by this engine slice (exactly one timer/message/signal definition).");
    }

    private static string ResolveEndEvent(BpmnElement element)
    {
        if (element.EventDefinitions.Count == 0)
            return EndEventNone;

        if (element.EventDefinitions.Count == 1 &&
            StringComparer.Ordinal.Equals(element.EventDefinitions.Single().Type, BpmnEventDefinitionTypes.Terminate))
            return EndEventTerminate;

        throw new BpmnExecutionException(
            $"BPMN end event '{element.ElementId}' declares unsupported event definitions; only none and terminate end events are supported by this engine slice.");
    }
}
