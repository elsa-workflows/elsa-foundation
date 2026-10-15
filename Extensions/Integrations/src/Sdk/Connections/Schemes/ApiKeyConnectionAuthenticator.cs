namespace Elsa.Integrations.Sdk.Connections.Schemes;

/// <summary>
/// Places an API key from the <c>apiKey</c> secret field. Settings <c>placement</c> is <c>header</c>
/// (default) or <c>query</c>; <c>headerName</c> defaults to <c>X-Api-Key</c>; <c>queryName</c> defaults
/// to <c>api_key</c>.
/// </summary>
public sealed class ApiKeyConnectionAuthenticator : IConnectionAuthenticator
{
    public const string ApiKeyField = "apiKey";
    public const string PlacementSetting = "placement";
    public const string HeaderNameSetting = "headerName";
    public const string QueryNameSetting = "queryName";
    public const string HeaderPlacement = "header";
    public const string QueryPlacement = "query";
    public const string DefaultHeaderName = "X-Api-Key";
    public const string DefaultQueryName = "api_key";

    public string Scheme => ConnectionAuthSchemes.ApiKey;

    public async ValueTask ApplyAsync(HttpRequestMessage request, IConnectionHandle connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(connection);

        var key = await connection.GetSensitiveAsync(ApiKeyField, cancellationToken);
        var placement = connection.Settings.GetValueOrDefault(PlacementSetting, HeaderPlacement);

        if (string.Equals(placement, QueryPlacement, StringComparison.OrdinalIgnoreCase))
        {
            var queryName = connection.Settings.GetValueOrDefault(QueryNameSetting, DefaultQueryName);
            var uri = request.RequestUri ?? throw new ConnectionUnavailableException(
                $"Connection '{connection.ConnectionName}' cannot place an API key on a request with no URI.",
                connection.ConnectionName);
            var builder = new UriBuilder(uri);
            var separator = string.IsNullOrEmpty(builder.Query) ? "" : "&";
            var query = builder.Query.TrimStart('?');
            builder.Query = $"{query}{separator}{Uri.EscapeDataString(queryName)}={Uri.EscapeDataString(key.Value)}";
            request.RequestUri = builder.Uri;
            return;
        }

        var headerName = connection.Settings.GetValueOrDefault(HeaderNameSetting, DefaultHeaderName);
        request.Headers.Remove(headerName);
        request.Headers.TryAddWithoutValidation(headerName, key.Value);
    }
}
