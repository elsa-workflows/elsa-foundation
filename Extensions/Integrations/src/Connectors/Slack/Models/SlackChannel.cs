using System.Text.Json.Serialization;

namespace Elsa.Integrations.Connectors.Slack.Models;

public sealed record SlackChannel
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("is_private")]
    public bool IsPrivate { get; init; }

    [JsonPropertyName("is_archived")]
    public bool IsArchived { get; init; }
}
