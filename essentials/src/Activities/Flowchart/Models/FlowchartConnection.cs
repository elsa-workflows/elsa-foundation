using System.Text.Json.Serialization;

namespace Elsa.Activities.Flowchart.Models;

public sealed class FlowchartConnection
{
    [JsonConstructor]
    public FlowchartConnection(FlowchartEndpoint source, FlowchartEndpoint target, string? id = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        Source = source;
        Target = target;
        Id = string.IsNullOrWhiteSpace(id) ? null : id.Trim();
    }

    [JsonPropertyName("id")]
    public string? Id { get; }

    [JsonPropertyName("source")]
    public FlowchartEndpoint Source { get; }

    [JsonPropertyName("target")]
    public FlowchartEndpoint Target { get; }
}
