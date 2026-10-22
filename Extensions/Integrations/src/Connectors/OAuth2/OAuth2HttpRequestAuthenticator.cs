using Elsa.Activities.Http.Connections;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;
using Elsa.Connections.Http;

namespace Elsa.Integrations.Connectors.OAuth2;

internal sealed class OAuth2HttpRequestAuthenticator(OAuth2TokenService tokens) : IHttpRequestAuthenticator
{
    public string SchemeName => HttpAuthSchemes.OAuth2;

    public async ValueTask ApplyAsync(HttpRequestMessage request, IResolvedConnection connection, CancellationToken cancellationToken = default)
    {
        HttpConnectionHostGuard.EnsureAllowed(request, connection);

        var refresh = request.Options.TryGetValue(HttpConnectionApplyOptions.RefreshCredentials, out var force) && force;
        var grant = refresh
            ? await tokens.RefreshAsync(connection, await tokens.ReadGrantAsync(connection, cancellationToken), cancellationToken)
            : await tokens.GetValidGrantAsync(connection, cancellationToken);

        var scheme = string.IsNullOrWhiteSpace(grant.TokenType) ? "Bearer" : grant.TokenType;
        var header = grant.AccessToken.StartsWith($"{scheme} ", StringComparison.OrdinalIgnoreCase)
            ? grant.AccessToken
            : $"{scheme} {grant.AccessToken}";
        HttpConnectionHostGuard.SetAuthorization(request, connection, header);
        HttpConnectionApplyOptions.MaskSecret(request, $"{connection.Name}:access_token", grant.AccessToken);
        HttpConnectionApplyOptions.MaskSecret(request, $"{connection.Name}:refresh_token", grant.RefreshToken);
    }
}
