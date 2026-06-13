using System.Text.Json.Serialization;

namespace Elsa.Activities.Flowchart.Models;

public sealed class FlowchartStructure
{
    [JsonConstructor]
    public FlowchartStructure(
        IReadOnlyCollection<FlowchartConnection>? connections = null,
        string? startNodeId = null)
    {
        Connections = connections ?? [];
        StartNodeId = startNodeId;
    }

    [JsonPropertyName("connections")]
    public IReadOnlyCollection<FlowchartConnection> Connections { get; }

    [JsonPropertyName("startNodeId")]
    public string? StartNodeId { get; }
}
