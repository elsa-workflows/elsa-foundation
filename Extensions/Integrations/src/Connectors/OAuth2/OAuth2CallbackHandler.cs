using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;

namespace Elsa.Integrations.Connectors.OAuth2;

internal sealed class OAuth2CallbackHandler(
    OAuth2TokenService tokens,
    IConnectionResolver resolver) : IOAuthCallbackHandler
{
    public async ValueTask<OAuthCallbackResponse?> TryHandleAsync(
        OAuthCallbackRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(request.Error))
            return new OAuthCallbackResponse(400, "error", request.ErrorDescription ?? request.Error);

        if (string.IsNullOrWhiteSpace(request.Code) ||
            !OAuth2TokenService.TryParseState(request.State, out var tenantId, out var connectionName, out _))
        {
            return null;
        }

        try
        {
            await using var connection = await resolver.ResolveAsync(new ConnectionResolveRequest
            {
                TenantId = tenantId,
                Name = connectionName,
                RequiredType = ConnectionTypeNames.Http,
                Owner = ConnectionOwners.Tenant
            }, cancellationToken);

            if (!string.Equals(connection.AuthScheme, HttpAuthSchemes.OAuth2, StringComparison.OrdinalIgnoreCase))
                return null;

            await tokens.CompleteAuthorizationCodeAsync(connection, request.Code, request.State!, cancellationToken);
            return new OAuthCallbackResponse(200, "authorized", null);
        }
        catch (Exception exception)
        {
            return new OAuthCallbackResponse(400, "failed", exception.GetType().Name);
        }
    }
}
