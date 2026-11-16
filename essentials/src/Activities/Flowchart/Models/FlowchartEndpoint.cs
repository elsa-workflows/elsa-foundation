using System.Text.Json.Serialization;
using Elsa.Workflows.Runtime.Core.Constants;

namespace Elsa.Activities.Flowchart.Models;

public sealed class FlowchartEndpoint
{
    [JsonConstructor]
    public FlowchartEndpoint(string nodeId, string? port = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        NodeId = nodeId;
        Port = NormalizePort(port);
    }

    [JsonPropertyName("nodeId")]
    public string NodeId { get; }

    [JsonPropertyName("port")]
    public string Port { get; }

    public static string NormalizePort(string? port) =>
        string.IsNullOrWhiteSpace(port) ? ActivityOutcomes.Done : port.Trim();
}
