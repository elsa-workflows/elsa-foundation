using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;

namespace Elsa.Connections.Http;

internal sealed class HttpConnectionTypeProvider : IConnectionTypeProvider
{
    public ConnectionTypeDescriptor Descriptor { get; } = new(
        ConnectionTypeNames.Http,
        "HTTP",
        [HttpAuthSchemes.ApiKey, HttpAuthSchemes.Basic, HttpAuthSchemes.Bearer, HttpAuthSchemes.OAuth2],
        [
            new ConnectionSettingField(HttpConnectionSettingKeys.BaseUrl, "Base URL", "url"),
            new ConnectionSettingField(HttpConnectionSettingKeys.AllowedHosts, "Allowed hosts", "text"),
            new ConnectionSettingField(HttpConnectionSettingKeys.AllowInsecureHttp, "Allow insecure HTTP", "checkbox")
        ],
        AllowedHostsPolicy.UserDeclared);

    public AllowedHostsPolicy ResolveHosts(IReadOnlyDictionary<string, string> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var hosts = new List<string>();
        if (settings.TryGetValue(HttpConnectionSettingKeys.AllowedHosts, out var allowed) && !string.IsNullOrWhiteSpace(allowed))
        {
            hosts.AddRange(allowed.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        }

        if (settings.TryGetValue(HttpConnectionSettingKeys.BaseUrl, out var baseUrl) &&
            Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            hosts.Add(uri.IdnHost);
        }

        return new AllowedHostsPolicy(AllowedHostsPolicy.UserDeclaredKind, hosts);
    }

    public Task<ConnectionTestResult> TestAsync(IResolvedConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!string.Equals(connection.TypeName, ConnectionTypeNames.Http, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new ConnectionTestResult
            {
                Succeeded = false,
                Message = $"Connection '{connection.Name}' is not an http connection."
            });
        }

        return Task.FromResult(new ConnectionTestResult
        {
            Succeeded = true,
            Message = $"HTTP connection '{connection.Name}' ({connection.AuthScheme}) is ready."
        });
    }
}

internal sealed class ApiKeySchemeProvider : IAuthenticationSchemeProvider
{
    public AuthSchemeDescriptor Descriptor { get; } = new(
        HttpAuthSchemes.ApiKey,
        "API key",
        [
            new AuthSchemeField(HttpConnectionSettingKeys.ApiKeyHeaderName, "Header name", Sensitive: false, "text", Required: false),
            new AuthSchemeField(HttpConnectionSettingKeys.ApiKeyPlacement, "Placement", Sensitive: false, "text", Required: false),
            new AuthSchemeField(HttpConnectionSettingKeys.ApiKeyQueryName, "Query name", Sensitive: false, "text", Required: false),
            new AuthSchemeField(HttpConnectionSecretFields.ApiKey, "API key", Sensitive: true, "password")
        ],
        HasInteractiveAuthorization: false,
        HasRefreshableState: false,
        HasRevocation: false,
        RequiresWritableStore: false);
}

internal sealed class BasicSchemeProvider : IAuthenticationSchemeProvider
{
    public AuthSchemeDescriptor Descriptor { get; } = new(
        HttpAuthSchemes.Basic,
        "Basic",
        [
            new AuthSchemeField(HttpConnectionSettingKeys.Username, "Username", Sensitive: false, "text"),
            new AuthSchemeField(HttpConnectionSecretFields.Password, "Password", Sensitive: true, "password")
        ],
        HasInteractiveAuthorization: false,
        HasRefreshableState: false,
        HasRevocation: false,
        RequiresWritableStore: false);
}

internal sealed class BearerSchemeProvider : IAuthenticationSchemeProvider
{
    public AuthSchemeDescriptor Descriptor { get; } = new(
        HttpAuthSchemes.Bearer,
        "Bearer",
        [
            new AuthSchemeField(HttpConnectionSecretFields.Token, "Token", Sensitive: true, "password")
        ],
        HasInteractiveAuthorization: false,
        HasRefreshableState: false,
        HasRevocation: false,
        RequiresWritableStore: false);
}
