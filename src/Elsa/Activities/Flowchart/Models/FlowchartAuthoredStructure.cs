using System.Text.Json.Serialization;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Flowchart.Models;

public sealed class FlowchartAuthoredStructure
{
    [JsonConstructor]
    public FlowchartAuthoredStructure(
        IReadOnlyCollection<ActivityNode>? activities = null,
        IReadOnlyCollection<FlowchartConnection>? connections = null,
        string? startNodeId = null,
        IReadOnlyDictionary<string, FlowchartNodeMetadata>? nodeMetadata = null,
        IReadOnlyDictionary<string, FlowchartConnectionMetadata>? connectionMetadata = null,
        IReadOnlyCollection<VariableDefinition>? variables = null)
    {
        Activities = activities ?? [];
        Connections = connections ?? [];
        StartNodeId = startNodeId;
        NodeMetadata = nodeMetadata ?? new Dictionary<string, FlowchartNodeMetadata>();
        ConnectionMetadata = connectionMetadata ?? new Dictionary<string, FlowchartConnectionMetadata>();
        Variables = variables ?? [];
    }

    [JsonPropertyName("activities")]
    public IReadOnlyCollection<ActivityNode> Activities { get; }

    /// <summary>
    /// Container-scoped variables declared by this Flowchart, visible to its descendant activities
    /// (scoped variable model, ADR 0027).
    /// </summary>
    [JsonPropertyName("variables")]
    public IReadOnlyCollection<VariableDefinition> Variables { get; }

    [JsonPropertyName("connections")]
    public IReadOnlyCollection<FlowchartConnection> Connections { get; }

    [JsonPropertyName("startNodeId")]
    public string? StartNodeId { get; }

    [JsonPropertyName("nodeMetadata")]
    public IReadOnlyDictionary<string, FlowchartNodeMetadata> NodeMetadata { get; }

    [JsonPropertyName("connectionMetadata")]
    public IReadOnlyDictionary<string, FlowchartConnectionMetadata> ConnectionMetadata { get; }
}
