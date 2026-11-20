using System.Text.Json.Serialization;

namespace Kernel.Graph.Models;

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

    // Preserve the existing wire IDs for routing and arrivals, including authored IDs.
    internal string GetId() => Id ?? $"{Source.NodeId}:{Source.Port}->{Target.NodeId}";

    // Wire IDs may collide across accepted endpoints; joins also match the structural facts.
    internal (string Source, string Port, string Target, string Connection) GetAnswerKey() =>
        (Source.NodeId, Source.Port, Target.NodeId, GetId());

    [JsonPropertyName("id")]
    public string? Id { get; }

    [JsonPropertyName("source")]
    public FlowchartEndpoint Source { get; }

    [JsonPropertyName("target")]
    public FlowchartEndpoint Target { get; }
}
