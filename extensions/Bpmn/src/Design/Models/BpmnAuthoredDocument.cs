using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Bpmn.Models;

/// <summary>
/// Complete-source authoring contract. XML alone owns standard BPMN semantics, extensions and DI;
/// the disjoint native activity tree is linked by identities, never by a second persisted graph.
/// This internal retention contract is not an executable structure schema.
/// </summary>
internal sealed record BpmnAuthoredDocument(
    string DocumentXml,
    string ProcessId,
    IReadOnlyCollection<ActivityNode> Activities,
    IReadOnlyCollection<BpmnDocumentBinding> Bindings,
    IReadOnlyCollection<VariableDefinition> Variables)
{
    internal const string SchemaVersion = "2.0.0";

    public IReadOnlyCollection<BpmnDocumentScopeHeader> ScopeHeaders { get; init; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

/// <summary>Native configuration only; XML owns the scope's body and parentage.</summary>
internal sealed record BpmnDocumentScopeHeader(
    string ScopeElementId,
    string NodeId,
    string ActivityVersionId,
    IReadOnlyCollection<ArgumentState> Inputs,
    IReadOnlyCollection<ArgumentState> Outputs,
    IReadOnlyCollection<VariableDefinition> Variables)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ActivityExecutionPlacement ExecutionPlacement { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

/// <summary>Self-contained derived context, never a physically authored activity structure.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Skip)]
internal sealed record BpmnDocumentScopeView(ActivityNode Owner, string ScopeElementId, IReadOnlyCollection<VariableDefinition> Variables)
{
    internal const string Kind = "elsa.bpmn.scope-view";
    internal const string SchemaVersion = "1.0.0";
}

internal sealed record BpmnDocumentBinding(string ElementId, string? ChildNodeId = null, string? ListenerNodeId = null)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}
