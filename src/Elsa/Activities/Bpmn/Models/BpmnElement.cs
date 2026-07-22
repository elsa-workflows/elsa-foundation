using System.Text.Json.Serialization;

namespace Elsa.Activities.Bpmn.Models;

/// <summary>
/// One BPMN flow element in the process graph. Gateways and start/end events are engine-interpreted
/// (they carry no Elsa child activity); task-family, subprocess, and intermediate-catch-event elements
/// bind an Elsa child from the <c>Bpmn.Activities</c> slot through <see cref="ChildNodeId"/>.
/// </summary>
public sealed class BpmnElement
{
    [JsonConstructor]
    public BpmnElement(
        string elementId,
        string elementType,
        string? name = null,
        string? childNodeId = null,
        string? laneId = null,
        string? defaultFlowId = null,
        IReadOnlyCollection<BpmnEventDefinition>? eventDefinitions = null,
        IReadOnlyDictionary<string, string>? properties = null,
        string? attachedToRef = null,
        bool cancelActivity = true,
        BpmnLoopCharacteristics? loopCharacteristics = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);
        ArgumentException.ThrowIfNullOrWhiteSpace(elementType);

        ElementId = elementId;
        ElementType = elementType;
        Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        ChildNodeId = string.IsNullOrWhiteSpace(childNodeId) ? null : childNodeId.Trim();
        LaneId = string.IsNullOrWhiteSpace(laneId) ? null : laneId.Trim();
        DefaultFlowId = string.IsNullOrWhiteSpace(defaultFlowId) ? null : defaultFlowId.Trim();
        EventDefinitions = eventDefinitions ?? [];
        Properties = properties ?? new Dictionary<string, string>();
        AttachedToRef = string.IsNullOrWhiteSpace(attachedToRef) ? null : attachedToRef.Trim();
        CancelActivity = cancelActivity;
        LoopCharacteristics = loopCharacteristics;
    }

    [JsonPropertyName("elementId")]
    public string ElementId { get; }

    /// <summary>The BPMN element type (see <see cref="BpmnElementTypes"/>).</summary>
    [JsonPropertyName("elementType")]
    public string ElementType { get; }

    [JsonPropertyName("name")]
    public string? Name { get; }

    /// <summary>The authored node id of the bound Elsa child activity, when this element executes one.</summary>
    [JsonPropertyName("childNodeId")]
    public string? ChildNodeId { get; }

    [JsonPropertyName("laneId")]
    public string? LaneId { get; }

    /// <summary>The element's BPMN default sequence flow, taken when no conditional flow matches.</summary>
    [JsonPropertyName("defaultFlowId")]
    public string? DefaultFlowId { get; }

    [JsonPropertyName("eventDefinitions")]
    public IReadOnlyCollection<BpmnEventDefinition> EventDefinitions { get; }

    [JsonPropertyName("properties")]
    public IReadOnlyDictionary<string, string> Properties { get; }

    /// <summary>
    /// The host element id a <c>boundaryEvent</c> is attached to (spec 120); <c>null</c> on every
    /// non-boundary element. A boundary reacts to a stimulus while its host runs.
    /// </summary>
    [JsonPropertyName("attachedToRef")]
    public string? AttachedToRef { get; }

    /// <summary>
    /// Whether a <c>boundaryEvent</c> interrupts its host when it fires (spec 120): <c>true</c> (the BPMN
    /// default) tears the host down and routes the boundary path; <c>false</c> runs the boundary path
    /// alongside the still-running host. Meaningful only on boundaries.
    /// </summary>
    [JsonPropertyName("cancelActivity")]
    public bool CancelActivity { get; }

    /// <summary>
    /// The multi-instance loop characteristics of this element (spec 121); <c>null</c> on every
    /// non-multi-instance element. Valid only on a task-family or <c>subProcess</c> host that binds a child.
    /// </summary>
    [JsonPropertyName("loopCharacteristics")]
    public BpmnLoopCharacteristics? LoopCharacteristics { get; }
}

/// <summary>
/// The BPMN element types understood by the structure contract. Kept as string constants (not an enum)
/// so later phases can add element types without a state-schema break.
/// </summary>
public static class BpmnElementTypes
{
    public const string StartEvent = "startEvent";
    public const string EndEvent = "endEvent";
    public const string IntermediateCatchEvent = "intermediateCatchEvent";
    public const string Task = "task";
    public const string UserTask = "userTask";
    public const string ServiceTask = "serviceTask";
    public const string ScriptTask = "scriptTask";
    public const string ManualTask = "manualTask";
    public const string BusinessRuleTask = "businessRuleTask";
    public const string SendTask = "sendTask";
    public const string ReceiveTask = "receiveTask";
    public const string SubProcess = "subProcess";
    public const string ExclusiveGateway = "exclusiveGateway";
    public const string ParallelGateway = "parallelGateway";
    public const string InclusiveGateway = "inclusiveGateway";
    public const string EventBasedGateway = "eventBasedGateway";
    public const string BoundaryEvent = "boundaryEvent";
}
