using System.Text.Json;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Runtime.Core.Models;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Tests;

internal static class BpmnTestNodes
{
    public const string ProcessNodeId = "node-bpmn";

    public static ExecutableNode Probe(string nodeId) =>
        new(nodeId, $"authored-{nodeId}", "Bpmn.Tests.Probe", "1.0.0", "Probe",
            JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>());

    public static BpmnElement StartEvent(string elementId = "start") =>
        new(elementId, BpmnElementTypes.StartEvent);

    public static BpmnElement EndEvent(string elementId = "end") =>
        new(elementId, BpmnElementTypes.EndEvent);

    public static BpmnElement EventStart(string elementId, string definitionType) =>
        new(elementId, BpmnElementTypes.StartEvent, eventDefinitions: [new BpmnEventDefinition(definitionType)]);

    public static BpmnElement IntermediateCatchEvent(string elementId, string definitionType, string? childNodeId = null) =>
        new(elementId, BpmnElementTypes.IntermediateCatchEvent, childNodeId: childNodeId, eventDefinitions: [new BpmnEventDefinition(definitionType)]);

    public static BpmnElement Task(string elementId, string? childNodeId = null, string? defaultFlowId = null) =>
        new(elementId, BpmnElementTypes.Task, childNodeId: childNodeId, defaultFlowId: defaultFlowId);

    public static BpmnElement CallActivity(string elementId, string? childNodeId = null, string? defaultFlowId = null) =>
        new(elementId, BpmnElementTypes.CallActivity, childNodeId: childNodeId, defaultFlowId: defaultFlowId);

    public static BpmnElement MultiInstanceCallActivity(string elementId, string childNodeId, int cardinality, bool isSequential, string? defaultFlowId = null) =>
        new(elementId, BpmnElementTypes.CallActivity, childNodeId: childNodeId, defaultFlowId: defaultFlowId,
            loopCharacteristics: new BpmnLoopCharacteristics(isSequential: isSequential, cardinality: cardinality));

    public static BpmnElement MultiInstanceTask(string elementId, string childNodeId, int cardinality, bool isSequential, string? defaultFlowId = null) =>
        new(elementId, BpmnElementTypes.Task, childNodeId: childNodeId, defaultFlowId: defaultFlowId,
            loopCharacteristics: new BpmnLoopCharacteristics(isSequential: isSequential, cardinality: cardinality));

    public static BpmnElement ExclusiveGateway(string elementId, string? childNodeId = null, string? defaultFlowId = null) =>
        new(elementId, BpmnElementTypes.ExclusiveGateway, childNodeId: childNodeId, defaultFlowId: defaultFlowId);

    public static BpmnElement EventBasedGateway(string elementId) =>
        new(elementId, BpmnElementTypes.EventBasedGateway);

    public static BpmnElement BoundaryEvent(string elementId, string attachedToRef, string definitionType, string? childNodeId = null, bool cancelActivity = true) =>
        new(elementId, BpmnElementTypes.BoundaryEvent, childNodeId: childNodeId, eventDefinitions: [new BpmnEventDefinition(definitionType)], attachedToRef: attachedToRef, cancelActivity: cancelActivity);

    public static BpmnElement CompensationBoundary(string elementId, string attachedToRef, string handlerElementId) =>
        new(elementId, BpmnElementTypes.BoundaryEvent, eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Compensation)], attachedToRef: attachedToRef, compensationHandlerElementId: handlerElementId);

    public static BpmnElement CompensationHandler(string elementId, string childNodeId) =>
        new(elementId, BpmnElementTypes.Task, childNodeId: childNodeId, isForCompensation: true);

    public static BpmnElement SubProcess(string elementId, string childNodeId, string? defaultFlowId = null) =>
        new(elementId, BpmnElementTypes.SubProcess, childNodeId: childNodeId, defaultFlowId: defaultFlowId);

    public static BpmnElement EscalationThrow(string elementId, string code, string? name = null) =>
        new(elementId, BpmnElementTypes.IntermediateThrowEvent, eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Escalation, EscalationProperties(code, name))]);

    public static BpmnElement EscalationBoundary(string elementId, string attachedToRef, string? code = null, bool cancelActivity = true) =>
        new(elementId, BpmnElementTypes.BoundaryEvent, eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Escalation, code is null ? null : EscalationProperties(code, null))], attachedToRef: attachedToRef, cancelActivity: cancelActivity);

    private static IReadOnlyDictionary<string, string> EscalationProperties(string code, string? name)
    {
        var properties = new Dictionary<string, string> { [BpmnEventDefinitionProperties.Code] = code };
        if (!string.IsNullOrWhiteSpace(name))
            properties[BpmnEventDefinitionProperties.Name] = name;
        return properties;
    }

    public static BpmnElement EventSubprocess(string elementId, string childNodeId) =>
        new(elementId, BpmnElementTypes.SubProcess, childNodeId: childNodeId, triggeredByEvent: true);

    public static BpmnElement EventSubprocessWithListener(string elementId, string childNodeId, string listenerNodeId) =>
        new(elementId, BpmnElementTypes.SubProcess, childNodeId: childNodeId, triggeredByEvent: true, listenerNodeId: listenerNodeId);

    public static BpmnElement EventSubprocessMessageStart(string elementId, string name, bool interrupting = true) =>
        new(elementId, BpmnElementTypes.StartEvent,
            eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Message, new Dictionary<string, string> { [BpmnEventDefinitionProperties.Name] = name })],
            cancelActivity: interrupting);

    public static BpmnElement EventSubprocessSignalStart(string elementId, string name, bool interrupting = true) =>
        new(elementId, BpmnElementTypes.StartEvent,
            eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Signal, new Dictionary<string, string> { [BpmnEventDefinitionProperties.Name] = name })],
            cancelActivity: interrupting);

    public static BpmnElement EventSubprocessTimerStart(string elementId, string interval, bool interrupting = true) =>
        new(elementId, BpmnElementTypes.StartEvent,
            eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Timer, new Dictionary<string, string> { [BpmnEventDefinitionProperties.Interval] = interval })],
            cancelActivity: interrupting);

    public static BpmnElement EventSubprocessEscalationStart(string elementId, string? code = null, bool interrupting = true) =>
        new(elementId, BpmnElementTypes.StartEvent,
            eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Escalation, code is null ? null : EscalationProperties(code, null))],
            cancelActivity: interrupting);

    public static BpmnElement EventSubprocessErrorStart(string elementId, bool interrupting = true) =>
        new(elementId, BpmnElementTypes.StartEvent,
            eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Error)],
            cancelActivity: interrupting);

    public static ExecutableNode NestedProcessNode(
        string nodeId,
        IReadOnlyCollection<BpmnElement> elements,
        IReadOnlyCollection<BpmnSequenceFlow> flows,
        IReadOnlyCollection<ExecutableNode>? innerChildren = null,
        bool isTransaction = false) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(BpmnProcessActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "InnerDescriptor",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(BpmnProcessActivity.ActivitiesSlotName, innerChildren ?? [])],
            structure: new ExecutableActivityStructure(
                BpmnProcessActivity.StructureKind,
                BpmnProcessActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new BpmnStructure(elements, flows, isTransaction: isTransaction))));

    public static BpmnElement TransactionSubProcess(string elementId, string childNodeId, string? defaultFlowId = null) =>
        new(elementId, BpmnElementTypes.SubProcess, childNodeId: childNodeId, defaultFlowId: defaultFlowId, isTransaction: true);

    public static BpmnElement CancelEnd(string elementId = "cancel-end") =>
        new(elementId, BpmnElementTypes.EndEvent, eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Cancel)]);

    public static BpmnElement CancelBoundary(string elementId, string attachedToRef) =>
        new(elementId, BpmnElementTypes.BoundaryEvent, eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Cancel)], attachedToRef: attachedToRef);

    public static BpmnElement CompensateThrow(string elementId, string? activityRef = null) =>
        new(elementId, BpmnElementTypes.IntermediateThrowEvent, eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Compensation, CompensationProperties(activityRef))]);

    private static IReadOnlyDictionary<string, string>? CompensationProperties(string? activityRef) =>
        string.IsNullOrWhiteSpace(activityRef)
            ? null
            : new Dictionary<string, string> { [BpmnEventDefinitionProperties.ActivityRef] = activityRef };

    public static BpmnSequenceFlow Flow(string flowId, string sourceRef, string targetRef, string? conditionOutcome = null, bool isDefault = false) =>
        new(flowId, sourceRef, targetRef, conditionOutcome: conditionOutcome, isDefault: isDefault);
}
