using System.Text.Json.Serialization;

namespace Elsa.Integrations.Sdk.OAuth;

/// <summary>Mutable token state stored as a secret. Never part of workflow JSON or activity results.</summary>
public sealed class OAuth2Grant
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
