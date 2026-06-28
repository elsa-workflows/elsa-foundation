using System.Text.Json.Serialization;
using Elsa.Expressions.Core.Models;

namespace Elsa.Activities.Flowchart.Models;

public sealed class FlowchartStructure
{
    [JsonConstructor]
    public FlowchartStructure(
        IReadOnlyCollection<FlowchartConnection>? connections = null,
        string? startNodeId = null,
        IReadOnlyDictionary<string, FlowchartNodeMetadata>? nodeMetadata = null,
        IReadOnlyDictionary<string, FlowchartConnectionMetadata>? connectionMetadata = null,
        IReadOnlyCollection<VariableDefinition>? variables = null)
    {
        Connections = connections ?? [];
        StartNodeId = startNodeId;
        NodeMetadata = nodeMetadata ?? new Dictionary<string, FlowchartNodeMetadata>();
        ConnectionMetadata = connectionMetadata ?? new Dictionary<string, FlowchartConnectionMetadata>();
        Variables = variables ?? [];
    }

    [JsonPropertyName("connections")]
    public IReadOnlyCollection<FlowchartConnection> Connections { get; }

    /// <summary>
    /// Container-scoped variable declarations materialized for the runtime (ADR 0027).
    /// </summary>
    [JsonPropertyName("variables")]
    public IReadOnlyCollection<VariableDefinition> Variables { get; }

    [JsonPropertyName("startNodeId")]
    public string? StartNodeId { get; }

    [JsonPropertyName("nodeMetadata")]
    public IReadOnlyDictionary<string, FlowchartNodeMetadata> NodeMetadata { get; }

    [JsonPropertyName("connectionMetadata")]
    public IReadOnlyDictionary<string, FlowchartConnectionMetadata> ConnectionMetadata { get; }
}
