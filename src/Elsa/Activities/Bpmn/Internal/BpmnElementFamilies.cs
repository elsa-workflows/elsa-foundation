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
    public const string EndEventNone = "endEvent.none";
    public const string EndEventTerminate = "endEvent.terminate";
    public const string IntermediateCatchEvent = "intermediateCatchEvent.catch";
    public const string Task = "task";
    public const string SubProcess = "subProcess";
    public const string ExclusiveGateway = "exclusiveGateway";
    public const string ParallelGateway = "parallelGateway";
    public const string InclusiveGateway = "inclusiveGateway";

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

    private static string ResolveStartEvent(BpmnElement element)
    {
        if (element.EventDefinitions.Count > 0)
            throw new BpmnExecutionException(
                $"BPMN start event '{element.ElementId}' declares event definitions; only none start events are supported by this engine slice.");

        return StartEventNone;
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
