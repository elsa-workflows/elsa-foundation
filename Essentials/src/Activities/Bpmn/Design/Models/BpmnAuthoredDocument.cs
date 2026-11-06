using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Bpmn.Models;

/// <summary>
/// Complete-source authoring contract. XML alone owns standard BPMN semantics, extensions and DI;
/// the disjoint native activity tree is linked by identities, never by a second persisted graph.
/// This internal contract is a retention spike, not an executable structure schema.
/// </summary>
internal sealed record BpmnAuthoredDocument(
    string DocumentXml,
    string ProcessId,
    IReadOnlyCollection<ActivityNode> Activities,
    IReadOnlyCollection<BpmnDocumentBinding> Bindings,
    IReadOnlyCollection<VariableDefinition> Variables)
{
    internal const string SchemaVersion = "2.0.0";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

internal sealed record BpmnDocumentBinding(string ElementId, string? ChildNodeId = null, string? ListenerNodeId = null)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}
