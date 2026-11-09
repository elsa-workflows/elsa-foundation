namespace Elsa.Connections.Core.Contracts;

/// <summary>
/// Starts an interactive authorization flow for a named connection. The first handler that
/// returns a response wins; if none do, the API reports the request as unhandled.
/// Counterpart of <see cref="IOAuthCallbackHandler"/>.
/// </summary>
public interface IOAuthAuthorizeBeginHandler
{
    ValueTask<OAuthAuthorizeBeginResponse?> TryBeginAsync(
        OAuthAuthorizeBeginRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record OAuthAuthorizeBeginRequest(string TenantId, string ConnectionName);

public sealed record OAuthAuthorizeBeginResponse(string AuthorizationUrl);
