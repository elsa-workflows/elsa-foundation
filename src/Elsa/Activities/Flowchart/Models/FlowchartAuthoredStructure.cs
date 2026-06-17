using System.Text.Json.Serialization;
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
        IReadOnlyDictionary<string, FlowchartConnectionMetadata>? connectionMetadata = null)
    {
        Activities = activities ?? [];
        Connections = connections ?? [];
        StartNodeId = startNodeId;
        NodeMetadata = nodeMetadata ?? new Dictionary<string, FlowchartNodeMetadata>();
        ConnectionMetadata = connectionMetadata ?? new Dictionary<string, FlowchartConnectionMetadata>();
    }

    [JsonPropertyName("activities")]
    public IReadOnlyCollection<ActivityNode> Activities { get; }

    [JsonPropertyName("connections")]
    public IReadOnlyCollection<FlowchartConnection> Connections { get; }

    [JsonPropertyName("startNodeId")]
    public string? StartNodeId { get; }

    [JsonPropertyName("nodeMetadata")]
    public IReadOnlyDictionary<string, FlowchartNodeMetadata> NodeMetadata { get; }

    [JsonPropertyName("connectionMetadata")]
    public IReadOnlyDictionary<string, FlowchartConnectionMetadata> ConnectionMetadata { get; }
}
