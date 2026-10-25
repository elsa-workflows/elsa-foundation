using System.Text.Json.Serialization;

namespace Elsa.Integrations.Connectors.Slack.Models;

public sealed record SlackUser
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("real_name")]
    public string? RealName { get; init; }

    [JsonPropertyName("tz")]
    public string? Tz { get; init; }
}

public sealed record SlackUserLookupResult
{
    public bool Found { get; init; }

    public SlackUser? User { get; init; }
}

public sealed record SlackFileUploadResult
{
    public IReadOnlyList<string> FileIds { get; init; } = [];
}
