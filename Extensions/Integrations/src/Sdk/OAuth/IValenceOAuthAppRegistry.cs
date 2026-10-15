namespace Elsa.Integrations.Sdk.OAuth;

/// <summary>
/// STUB: Valence-registered OAuth apps. Production Control owns this catalog (#801).
/// </summary>
internal interface IValenceOAuthAppRegistry
{
    ValueTask<ValenceOAuthApp?> GetAsync(string appId, CancellationToken cancellationToken = default);
}

internal sealed record ValenceOAuthApp(string AppId, string ClientId, string ClientSecret)
{
    public override string ToString() => $"ValenceOAuthApp({AppId}, clientId={ClientId}, clientSecret=[redacted])";
}
