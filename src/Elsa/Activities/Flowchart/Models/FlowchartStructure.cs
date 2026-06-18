using System.Text.Json.Serialization;

namespace Elsa.Activities.Flowchart.Models;

public sealed class FlowchartStructure
{
    [JsonConstructor]
    public FlowchartStructure(
        IReadOnlyCollection<FlowchartConnection>? connections = null,
        string? startNodeId = null,
        IReadOnlyDictionary<string, FlowchartNodeMetadata>? nodeMetadata = null,
        IReadOnlyDictionary<string, FlowchartConnectionMetadata>? connectionMetadata = null)
    {
        Connections = connections ?? [];
        StartNodeId = startNodeId;
        NodeMetadata = nodeMetadata ?? new Dictionary<string, FlowchartNodeMetadata>();
        ConnectionMetadata = connectionMetadata ?? new Dictionary<string, FlowchartConnectionMetadata>();
    }

    [JsonPropertyName("connections")]
    public IReadOnlyCollection<FlowchartConnection> Connections { get; }

    [JsonPropertyName("startNodeId")]
    public string? StartNodeId { get; }

    [JsonPropertyName("nodeMetadata")]
    public IReadOnlyDictionary<string, FlowchartNodeMetadata> NodeMetadata { get; }

    [JsonPropertyName("connectionMetadata")]
    public IReadOnlyDictionary<string, FlowchartConnectionMetadata> ConnectionMetadata { get; }
}
