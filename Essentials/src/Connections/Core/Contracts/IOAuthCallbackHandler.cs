namespace Elsa.Connections.Core.Contracts;

/// <summary>
/// Claims the reserved <c>/_elsa/connections/oauth/callback</c> route. The first handler that
/// returns a response wins; if none do, the API reports the callback as unhandled.
/// </summary>
public interface IOAuthCallbackHandler
{
    ValueTask<OAuthCallbackResponse?> TryHandleAsync(OAuthCallbackRequest request, CancellationToken cancellationToken = default);
}

public sealed record OAuthCallbackRequest(string? Code, string? State, string? Error, string? ErrorDescription);

public sealed record OAuthCallbackResponse(int StatusCode, string Status, string? Message);
