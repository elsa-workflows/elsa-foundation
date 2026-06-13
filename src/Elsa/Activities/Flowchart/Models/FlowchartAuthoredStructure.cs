using System.Text.Json.Serialization;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Flowchart.Models;

public sealed class FlowchartAuthoredStructure
{
    [JsonConstructor]
    public FlowchartAuthoredStructure(
        IReadOnlyCollection<ActivityNode>? activities = null,
        IReadOnlyCollection<FlowchartConnection>? connections = null,
        string? startNodeId = null)
    {
        Activities = activities ?? [];
        Connections = connections ?? [];
        StartNodeId = startNodeId;
    }

    [JsonPropertyName("activities")]
    public IReadOnlyCollection<ActivityNode> Activities { get; }

    [JsonPropertyName("connections")]
    public IReadOnlyCollection<FlowchartConnection> Connections { get; }

    [JsonPropertyName("startNodeId")]
    public string? StartNodeId { get; }
}
