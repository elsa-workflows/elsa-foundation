namespace Elsa.Integrations.Sdk.Connections.Schemes;

/// <summary>Applies an <c>Authorization: Bearer</c> header from the connection's <c>accessToken</c> secret field.</summary>
public sealed class BearerConnectionAuthenticator : IConnectionAuthenticator
{
    public const string AccessTokenField = "accessToken";

    public string Scheme => ConnectionAuthSchemes.Bearer;

    public async ValueTask ApplyAsync(HttpRequestMessage request, IConnectionHandle connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(connection);

        var token = await connection.GetSensitiveAsync(AccessTokenField, cancellationToken);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Value);
    }
}
