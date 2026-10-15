namespace Elsa.Integrations.Sdk.OAuth;

internal sealed record OAuth2ClientCredentials(string ClientId, string? ClientSecret)
{
    public override string ToString() => $"OAuth2ClientCredentials({ClientId}, secret=[redacted])";
}
