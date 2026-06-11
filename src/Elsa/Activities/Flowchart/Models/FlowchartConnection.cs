using System.Text.Json.Serialization;

namespace Elsa.Activities.Flowchart.Models;

public sealed class FlowchartConnection
{
    [JsonConstructor]
    public FlowchartConnection(FlowchartEndpoint source, FlowchartEndpoint target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        Source = source;
        Target = target;
    }

    [JsonPropertyName("source")]
    public FlowchartEndpoint Source { get; }

    [JsonPropertyName("target")]
    public FlowchartEndpoint Target { get; }
}
