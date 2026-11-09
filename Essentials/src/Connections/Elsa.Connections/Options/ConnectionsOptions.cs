namespace Elsa.Connections.Options;

public sealed class ConnectionsOptions
{
    public const string SectionName = "Elsa:Connections";

    /// <summary>
    /// Public origin used to build the OAuth callback URL
    /// (<c>{PublicBaseUrl}/_elsa/connections/oauth/callback</c>). Host-configurable (D2).
    /// </summary>
    public string? PublicBaseUrl { get; set; }
}
