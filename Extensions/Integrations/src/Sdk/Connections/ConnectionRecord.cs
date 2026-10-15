namespace Elsa.Integrations.Sdk.Connections;

/// <summary>
/// Tenant-owned connection metadata. Secret material is never stored here; only secret keys are.
/// </summary>
internal sealed class ConnectionRecord
{
    public required string TenantId { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required string AuthScheme { get; init; }

    /// <summary><c>valence</c> or <c>byo</c>.</summary>
    public string ClientSource { get; init; } = OAuth.OAuth2ClientSources.Valence;

    public IReadOnlyDictionary<string, string> Settings { get; init; } = new Dictionary<string, string>();

    /// <summary>Hosts this connection may attach a token to, in addition to its auth/token/scopes hosts.</summary>
    public IReadOnlyList<string> EgressHostAllowlist { get; init; } = [];
}
