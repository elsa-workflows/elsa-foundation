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
        JsonElement? diagram = null,
        bool isTransaction = false,
        IReadOnlyCollection<BpmnMessageFlow>? messageFlows = null)
    {
        Activities = activities ?? [];
        Elements = elements ?? [];
        SequenceFlows = sequenceFlows ?? [];
        Pools = pools ?? [];
        Lanes = lanes ?? [];
        Variables = variables ?? [];
        Diagram = diagram;
        IsTransaction = isTransaction;
        MessageFlows = messageFlows ?? [];
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

    /// <summary>
    /// Whether this authored process is a <b>transaction</b> (spec 125). Set by the importer from a
    /// <c>&lt;transaction&gt;</c> and by authoring; carried forward onto the compiled executable
    /// <see cref="BpmnStructure.IsTransaction"/>, which drives cancel-end validation and the structure-dependent
    /// <c>Cancelled</c> outcome declaration.
    /// </summary>
    [JsonPropertyName("isTransaction")]
    public bool IsTransaction { get; }

    /// <summary>
    /// Cross-pool message-flow wiring documentation (spec 136): the resolved <c>&lt;messageFlow&gt;</c> endpoints
    /// whose send or receive side lives in this process's pool. Authored-side metadata only — the engine delivers
    /// by name (the name-keyed stimulus fabric) and never reads this, the graph validator ignores it, and it is
    /// stripped from the compiled executable <see cref="BpmnStructure"/>.
    /// </summary>
    [JsonPropertyName("messageFlows")]
    public IReadOnlyCollection<BpmnMessageFlow> MessageFlows { get; }
}
