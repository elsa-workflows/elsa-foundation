using System.Text.Json.Serialization;
using Elsa.Expressions.Core.Models;

namespace Elsa.Activities.Bpmn.Models;

/// <summary>
/// The compiled executable <c>elsa.bpmn.structure</c> payload: the semantic process graph only. Pools,
/// lanes and the opaque diagram document are authored-side concerns and are stripped at compile time.
/// </summary>
public sealed class BpmnStructure
{
    [JsonConstructor]
    public BpmnStructure(
        IReadOnlyCollection<BpmnElement>? elements = null,
        IReadOnlyCollection<BpmnSequenceFlow>? sequenceFlows = null,
        IReadOnlyCollection<VariableDefinition>? variables = null)
    {
        Elements = elements ?? [];
        SequenceFlows = sequenceFlows ?? [];
        Variables = variables ?? [];
    }

    [JsonPropertyName("elements")]
    public IReadOnlyCollection<BpmnElement> Elements { get; }

    [JsonPropertyName("sequenceFlows")]
    public IReadOnlyCollection<BpmnSequenceFlow> SequenceFlows { get; }

    /// <summary>Container-scoped variable declarations materialized for the runtime (ADR 0027).</summary>
    [JsonPropertyName("variables")]
    public IReadOnlyCollection<VariableDefinition> Variables { get; }
}
