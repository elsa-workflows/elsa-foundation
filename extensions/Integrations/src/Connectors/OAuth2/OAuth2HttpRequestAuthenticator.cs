using Elsa.Activities.Http.Connections;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;
using Elsa.Connections.Http;

namespace Elsa.Integrations.Connectors.OAuth2;

/// <summary>
/// Applies an OAuth2 access token to the outbound request. A 401 retry with no refresh token
/// faults for authorization-code connections (the connection needs authorization again).
/// </summary>
internal sealed class OAuth2HttpRequestAuthenticator(OAuth2TokenService tokens) : IHttpRequestAuthenticator
{
    public string SchemeName => HttpAuthSchemes.OAuth2;

    public async ValueTask ApplyAsync(HttpRequestMessage request, IResolvedConnection connection, CancellationToken cancellationToken = default)
    {
        HttpConnectionCredentials.EnsureHostAllowed(request, connection);

        var refresh = request.Options.TryGetValue(HttpConnectionApplyOptions.RefreshCredentials, out var force) && force;
        var grant = refresh
            ? await tokens.RefreshAsync(connection, current: null, cancellationToken, forceRefresh: true)
            : await tokens.GetValidGrantAsync(connection, cancellationToken);

        var header = OAuth2Authorization.AuthorizationValue(connection, grant.AccessToken);
        HttpConnectionCredentials.SetAuthorization(request, connection, header);
        HttpConnectionApplyOptions.MaskSecret(request, $"{connection.Name}:access_token", grant.AccessToken);
        HttpConnectionApplyOptions.MaskSecret(request, $"{connection.Name}:refresh_token", grant.RefreshToken);
    }
}
