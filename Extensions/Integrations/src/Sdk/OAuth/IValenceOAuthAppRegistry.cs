namespace Elsa.Integrations.Sdk.OAuth;

/// <summary>
/// STUB: Valence-registered OAuth apps. Production Control owns this catalog (#801).
/// </summary>
public interface IValenceOAuthAppRegistry
{
    ValueTask<ValenceOAuthApp?> GetAsync(string appId, CancellationToken cancellationToken = default);
}

public sealed record ValenceOAuthApp(string AppId, string ClientId, string ClientSecret)
{
    public override string ToString() => $"ValenceOAuthApp({AppId}, clientId={ClientId}, clientSecret=[redacted])";
}
