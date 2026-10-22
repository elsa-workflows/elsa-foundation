using System.Text.Json.Serialization;

namespace Elsa.Integrations.Connectors.OAuth2;

internal sealed record OAuth2Grant
{
    public required string AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public string TokenType { get; init; } = "Bearer";
    public string? Scope { get; init; }
    public int Version { get; init; } = 1;

    [JsonIgnore]
    public bool HasAccessToken => !string.IsNullOrWhiteSpace(AccessToken);

    public override string ToString() => "[redacted oauth2-grant]";
}
