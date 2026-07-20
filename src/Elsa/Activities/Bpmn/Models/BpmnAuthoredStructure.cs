using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Bpmn.Models;

/// <summary>
/// The authored <c>elsa.bpmn.structure</c> payload. <see cref="Diagram"/> is an opaque BPMN-DI-shaped
/// document owned by design surfaces (shapes/edges/waypoints); it is carried authored-side for lossless
/// designer/XML round-trips and stripped from the compiled executable structure.
/// </summary>
public sealed class BpmnAuthoredStructure
{
    [JsonConstructor]
    public BpmnAuthoredStructure(
        IReadOnlyCollection<ActivityNode>? activities = null,
        IReadOnlyCollection<BpmnElement>? elements = null,
        IReadOnlyCollection<BpmnSequenceFlow>? sequenceFlows = null,
        IReadOnlyCollection<BpmnPool>? pools = null,
        IReadOnlyCollection<BpmnLane>? lanes = null,
        IReadOnlyCollection<VariableDefinition>? variables = null,
        JsonElement? diagram = null)
    {
        Activities = activities ?? [];
        Elements = elements ?? [];
        SequenceFlows = sequenceFlows ?? [];
        Pools = pools ?? [];
        Lanes = lanes ?? [];
        Variables = variables ?? [];
        Diagram = diagram;
    }

    [JsonPropertyName("activities")]
    public IReadOnlyCollection<ActivityNode> Activities { get; }

    [JsonPropertyName("elements")]
    public IReadOnlyCollection<BpmnElement> Elements { get; }

    [JsonPropertyName("sequenceFlows")]
    public IReadOnlyCollection<BpmnSequenceFlow> SequenceFlows { get; }

    [JsonPropertyName("pools")]
    public IReadOnlyCollection<BpmnPool> Pools { get; }

    [JsonPropertyName("lanes")]
    public IReadOnlyCollection<BpmnLane> Lanes { get; }

    /// <summary>
    /// Container-scoped variables declared by this BPMN process, visible to its descendant activities
    /// (scoped variable model, ADR 0027).
    /// </summary>
    [JsonPropertyName("variables")]
    public IReadOnlyCollection<VariableDefinition> Variables { get; }

    [JsonPropertyName("diagram")]
    public JsonElement? Diagram { get; }
}
