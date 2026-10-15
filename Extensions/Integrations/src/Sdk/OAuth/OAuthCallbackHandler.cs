namespace Elsa.Integrations.Sdk.OAuth;

/// <summary>
/// Completes authorization-code + PKCE after the provider redirects to
/// <c>/_elsa/connections/oauth/callback</c>.
/// </summary>
internal sealed class OAuthCallbackHandler(OAuth2TokenService tokens, OAuthCallbackOptions options)
{
    public async ValueTask<OAuthCallbackResult> HandleAsync(
        Connections.ConnectionRecord connection,
        string? code,
        string? state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            return OAuthCallbackResult.Failed("missing_code_or_state");

        try
        {
            await tokens.CompleteAuthorizationCodeAsync(
                connection,
                new AuthorizationCodeCallback(code, state),
                options,
                cancellationToken);
            return OAuthCallbackResult.Succeeded();
        }
        catch (Exception exception)
        {
            return OAuthCallbackResult.Failed(exception.GetType().Name);
        }
    }
}

internal sealed record OAuthCallbackResult(bool Ok, string? Error)
{
    public static OAuthCallbackResult Succeeded() => new(true, null);
    public static OAuthCallbackResult Failed(string error) => new(false, error);
}
