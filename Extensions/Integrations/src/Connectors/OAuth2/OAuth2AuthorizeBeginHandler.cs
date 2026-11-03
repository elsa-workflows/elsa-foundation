using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;

namespace Elsa.Integrations.Connectors.OAuth2;

internal sealed class OAuth2AuthorizeBeginHandler(
    OAuth2TokenService tokens,
    IConnectionResolver resolver) : IOAuthAuthorizeBeginHandler
{
    public async ValueTask<OAuthAuthorizeBeginResponse?> TryBeginAsync(
        OAuthAuthorizeBeginRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var connection = await resolver.ResolveAsync(new ConnectionResolveRequest
        {
            TenantId = request.TenantId,
            Name = request.ConnectionName,
            RequiredType = ConnectionTypeNames.Http,
            Owner = ConnectionOwners.Tenant
        }, cancellationToken);

        if (!string.Equals(connection.AuthScheme, HttpAuthSchemes.OAuth2, StringComparison.OrdinalIgnoreCase))
            return null;

        var grantType = connection.Settings.GetValueOrDefault(OAuth2SettingKeys.GrantType);
        if (!string.Equals(grantType, OAuth2GrantTypes.AuthorizationCode, StringComparison.OrdinalIgnoreCase))
            return null;

        var started = await tokens.BeginAuthorizationCodeAsync(connection, cancellationToken);
        return new OAuthAuthorizeBeginResponse(started.AuthorizationUrl);
    }
}
