namespace Elsa.Integrations.Sdk.OAuth;

public sealed record OAuth2ClientCredentials(string ClientId, string? ClientSecret)
{
    public override string ToString() => $"OAuth2ClientCredentials({ClientId}, secret=[redacted])";
}
